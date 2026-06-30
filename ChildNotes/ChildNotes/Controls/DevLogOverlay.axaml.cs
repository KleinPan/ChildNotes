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
        LogList.ItemsSource = DevLogger.Entries;
        DevLogger.Logged += OnLogged;
        UpdateStatus();
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        _isExpanded = !_isExpanded;
        ToggleButton.IsVisible = !_isExpanded;
        Panel.IsVisible = _isExpanded;
        if (_isExpanded)
        {
            UpdateStatus();
            ScrollToEnd();
        }
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        DevLogger.Clear();
        UpdateStatus();
    }

    private void OnScrollEnd(object? sender, RoutedEventArgs e)
    {
        ScrollToEnd();
    }

    private void OnLogged(DevLogger.LogEntry entry)
    {
        UpdateStatus();
        if (_isExpanded)
            ScrollToEnd();
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
