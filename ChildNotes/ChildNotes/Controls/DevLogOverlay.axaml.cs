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
            RefreshLogText();
            UpdateStatus();
            ScrollToEnd();
        }
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        DevLogger.Clear();
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

    private void OnLogged(DevLogger.LogEntry entry)
    {
        UpdateStatus();
        if (_isExpanded)
        {
            RefreshLogText();
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
    /// 刷新日志全文。将所有 LogEntry.Full 用换行连接为单一文本，
    /// 让 SelectableTextBlock 支持跨行选择与全选。
    /// </summary>
    private void RefreshLogText()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DevLogger.Entries.Count == 0)
            {
                LogText.Text = string.Empty;
                return;
            }
            var sb = new StringBuilder();
            foreach (var entry in DevLogger.Entries)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(entry.Full);
            }
            LogText.Text = sb.ToString();
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
