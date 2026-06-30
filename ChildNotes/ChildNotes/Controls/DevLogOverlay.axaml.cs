using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ChildNotes.Infrastructure;

namespace ChildNotes.Controls;

public partial class DevLogOverlay : UserControl
{
    private bool _isExpanded;

    /// <summary>已渲染的日志条目数（用于增量更新，避免每次重建全部文本）。/// </summary>
    private int _renderedCount;

    /// <summary>最多显示的日志条数。超过后截断旧日志，避免巨型 TextBlock 导致滚动卡顿。
    /// DevLogger.MaxLines=500 是环形缓冲区容量，但 UI 显示不需要这么多——
    /// 150 条 × 每条约 100 字符 = 15K 字符，TextBlock 渲染无压力；500 条 = 50K 字符开始卡顿。/// </summary>
    private const int MaxDisplayEntries = 150;

    public DevLogOverlay()
    {
        InitializeComponent();
        DevLogger.Logged += OnLogged;
        RefreshLogText();
        UpdateStatus();
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        _isExpanded = !_isExpanded;
        ToggleButton.IsVisible = !_isExpanded;
        Panel.IsVisible = _isExpanded;
        if (_isExpanded)
        {
            _renderedCount = 0; // 强制全量刷新
            RefreshLogText();
            UpdateStatus();
            ScrollToEnd();
        }
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        DevLogger.Clear();
        _renderedCount = 0;
        RefreshLogText();
        UpdateStatus();
    }

    private void OnScrollEnd(object? sender, RoutedEventArgs e)
    {
        ScrollToEnd();
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e)
    {
        // SelectableTextBlock.SelectAll 选中全部文本，用户随后可 Ctrl+C 复制
        LogText.SelectAll();
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        var text = LogText.Text;
        if (string.IsNullOrEmpty(text))
        {
            UpdateStatus("无内容可复制");
            return;
        }
        try
        {
            var lineCount = text.Count(c => c == '\n') + 1;

            // 尝试系统剪贴板（桌面端 Windows/macOS/Linux 可靠；Android 可能受限）
            bool copied = await TryCopyToClipboard(text);

            if (copied)
            {
                UpdateStatus($"已复制 {lineCount} 行");
                return;
            }

            // 剪贴板不可用时导出到文件
            // Android: 用 Personal 目录（/data/data/com.xxx/files/），可通过 adb pull 提取
            // 桌面: 用 GetTempPath()（%TEMP%/ChildNotes/）
            var dir = GetExportDirectory();
            System.IO.Directory.CreateDirectory(dir);
            var file = System.IO.Path.Combine(dir, $"devlog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            await System.IO.File.WriteAllTextAsync(file, text);
            UpdateStatus($"已保存 {lineCount} 行→{file}");
        }
        catch (Exception ex)
        {
            UpdateStatus($"失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 尝试写入系统剪贴板。桌面端 Windows/macOS/Linux 通常成功；
    /// Android 上 Avalonia 12.0.5 的 IClipboard 可能缺少 SetTextAsync 或权限受限，会静默降级为文件导出。
    /// </summary>
    private async Task<bool> TryCopyToClipboard(string text)
    {
        try
        {
            // DevLogOverlay 已在视觉树中，GetTopLevel(this) 最可靠
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return false;

            // 遍历 Clipboard 对象上所有 public 实例方法，找名字含 Text+Set 且接受 string 参数的异步方法
            var clipProp = topLevel.GetType().GetProperty("Clipboard");
            if (clipProp?.GetValue(topLevel) is not object clipboard) return false;

            foreach (var method in clipboard.GetType().GetMethods(
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (method.Name.Contains("Text") && method.Name.Contains("Set") &&
                    method.ReturnType == typeof(Task))
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                    {
                        await (Task)method.Invoke(clipboard, new object[] { text })!;
                        return true;
                    }
                }
            }
            return false;
        }
        catch
        {
            return false; // 剪贴板非关键功能，静默降级到文件导出
        }
    }

    /// <summary>
    /// 获取日志导出目录。优先用应用文档目录（Android 可访问），回退到临时目录。
    /// </summary>
    private static string GetExportDirectory()
    {
        // Android: SpecialFolder.Personal → /data/data/com.xxx/files/
        // 桌面: SpecialFolder.MyDocuments 或回退到 Temp
        var personal = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        if (!string.IsNullOrEmpty(personal) && System.IO.Directory.Exists(personal))
            return System.IO.Path.Combine(personal, "logs");

        var temp = System.IO.Path.GetTempPath();
        return System.IO.Path.Combine(temp, "ChildNotes", "logs");
    }

    private void OnLogged(DevLogger.LogEntry entry)
    {
        UpdateStatus();
        if (_isExpanded)
        {
            // 增量更新：只追加新日志，不重建全部 50K 字符串
            AppendLogEntry(entry);
            ScrollToEnd();
        }
    }

    private void UpdateStatus(string? hint = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var count = DevLogger.Entries.Count;
            StatusText.Text = hint ?? $"{count} 行";
            PlatformText.Text = DevLogger.PlatformTag;
        });
    }

    /// <summary>
    /// 全量刷新日志文本（首次打开 / 清空后）。
    /// 只取最近 MaxDisplayEntries 条，避免巨型 TextBlock 导致滚动卡顿。
    /// </summary>
    private void RefreshLogText()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var entries = DevLogger.Entries;
            if (entries.Count == 0)
            {
                LogText.Text = string.Empty;
                _renderedCount = 0;
                return;
            }

            // 只显示最近的 N 条（旧日志对开发者价值低，且是性能瓶颈）
            var start = Math.Max(0, entries.Count - MaxDisplayEntries);
            var sb = new StringBuilder(MaxDisplayEntries * 100); // 预分配容量
            for (var i = start; i < entries.Count; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(entries[i].Full);
            }
            LogText.Text = sb.ToString();
            _renderedCount = entries.Count;
        });
    }

    /// <summary>
    /// 增量追加单条日志（OnLogged 回调）。
    /// 避免每次新日志都重建 50K 字符字符串——改为只追加一行。
    /// </summary>
    private void AppendLogEntry(DevLogger.LogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var total = DevLogger.Entries.Count;

            // 首次或计数不匹配时回退到全量刷新
            if (_renderedCount != total - 1 || string.IsNullOrEmpty(LogText.Text))
            {
                RefreshLogText();
                return;
            }

            // 超过上限时截断：移除最旧的行（近似实现，通过全量刷新处理）
            if (total > MaxDisplayEntries && _renderedCount >= MaxDisplayEntries)
            {
                RefreshLogText();
                return;
            }

            // 增量追加：在末尾加一行
            LogText.Text += "\n" + entry.Full;
            _renderedCount = total;
        });
    }

    private void ScrollToEnd()
    {
        if (DevLogger.Entries.Count > 0)
        {
            LogScroll.ScrollToEnd();
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DevLogger.Logged -= OnLogged;
    }
}
