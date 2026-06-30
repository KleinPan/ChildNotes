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
        // SelectableTextBlock.SelectAll 在桌面端有效；Android 上选择功能不可靠，
        // 改为直接聚焦 + 全选文本内容，让用户感知到已选中
        LogText.Focus();
        LogText.SelectAll();

        // Android 上额外提示用户使用"复制"按钮
        if (OperatingSystem.IsAndroid())
        {
            UpdateStatus("已全选→请点「复制」导出");
        }
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

            bool copied = false;

            if (OperatingSystem.IsAndroid())
            {
                // Android: 绕过 Avalonia 的只读 IClipboard，直接调用原生 ClipboardManager
                copied = await TryCopyToAndroidClipboard(text);
            }
            else
            {
                // 桌面端: 通过 Avalonia TopLevel.Clipboard 写入
                copied = await TryCopyToAvaloniaClipboard(text);
            }

            if (copied)
            {
                UpdateStatus($"已复制 {lineCount} 行");
                return;
            }

            // 所有剪贴板方式都失败：导出到文件
            var dir = GetExportDirectory();
            System.IO.Directory.CreateDirectory(dir);
            var file = System.IO.Path.Combine(dir, $"devlog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            await System.IO.File.WriteAllTextAsync(file, text);

            if (OperatingSystem.IsAndroid())
            {
                UpdateStatus($"已保存 {lineCount} 行→文件");
            }
            else
            {
                UpdateStatus($"已保存 {lineCount} 行→{file}");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Android 原生剪贴板写入。通过反射调用 Android.Content.ClipboardManager.SetPrimaryClip，
    /// 绕过 Avalonia IClipboard 的只读限制。
    /// </summary>
    private static Task<bool> TryCopyToAndroidClipboard(string text)
    {
        try
        {
            // 获取 Android Context: Android.App.Application.Context (静态属性)
            var appType = Type.GetType("Android.App.Application, Mono.Android") ?? throw new Exception("Mono.Android not found");
            var context = appType.GetProperty("Context")?.GetValue(null) ?? throw new Exception("Android Context null");

            // context.GetSystemService(CLIPBOARD_SERVICE) → ClipboardManager
            var clipService = context.GetType().GetMethod("GetSystemService")?.Invoke(context, new object[] { "clipboard" }) ?? throw new Exception("ClipboardService null");

            // 创建 ClipData: ClipData.NewPlainText(label, text)
            var clipDataType = Type.GetType("Android.Content.ClipData, Mono.Android") ?? throw new Exception("ClipData not found");
            var newPlainText = clipDataType.GetMethod("NewPlainText", new[] { typeof(string), typeof(string) });
            var clipData = newPlainText?.Invoke(null, new object[] { "DevLog", text }) ?? throw new Exception("ClipData creation failed");

            // clipboardManager.SetPrimaryClip(clipData)
            clipService.GetType().GetMethod("SetPrimaryClip")?.Invoke(clipService, new object[] { clipData });

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Clipboard", $"Android native clipboard failed: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 桌面端剪贴板写入。通过 TopLevel.Clipboard 反射调用 SetTextAsync（兼容 Avalonia 12.x 各小版本 API 差异）。
    /// </summary>
    private async Task<bool> TryCopyToAvaloniaClipboard(string text)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return false;

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
