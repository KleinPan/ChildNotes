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
            var topLevel = TopLevel.GetTopLevel(this);
            bool copied = false;

            // 方案 A: Avalonia 12.0.5+ 剪贴板 API（IClipboard.SetTextAsync）
            // 注意：不同版本 Avalonia 的 IClipboard 方法名可能不同，
            // 这里用反射调用兼容多个版本（12.0.5 用 SetTextAsync，新版也支持）
            if (topLevel is not null)
            {
                var clipProp = topLevel.GetType().GetProperty("Clipboard");
                if (clipProp?.GetValue(topLevel) is object clipboard)
                {
                    // 反射调用 SetTextAsync 方法（避免版本编译差异）
                    var setMethod = clipboard.GetType().GetMethod("SetTextAsync");
                    if (setMethod is not null)
                    {
                        var task = (Task)setMethod.Invoke(clipboard, new object[] { text });
                        await task;
                        copied = true;
                    }
                }

                // 方案 B: 降级到 TopLevel 扩展方法（部分版本可用）
                if (!copied)
                {
                    var extMethod = typeof(TopLevel).GetMethod("SetClipboardTextAsync",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (extMethod is not null)
                    {
                        var task = (Task)extMethod.Invoke(null, new object[] { topLevel, text });
                        await task;
                        copied = true;
                    }
                }
            }

            if (copied)
            {
                var lineCount = text.Count(c => c == '\n') + 1;
                UpdateStatus($"已复制 {lineCount} 行");
            }
            else
            {
                // 方案 C: 降级为导出到临时文件（所有平台可靠）
                var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ChildNotes");
                System.IO.Directory.CreateDirectory(dir);
                var file = System.IO.Path.Combine(dir, $"devlog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                await System.IO.File.WriteAllTextAsync(file, text);
                UpdateStatus($"已导出 {file}");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"操作失败: {ex.Message}");
        }
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
