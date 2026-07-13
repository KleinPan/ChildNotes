using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class GrowthView : UserControl
{
    public GrowthView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // ★ 订阅编辑弹层的可见性变化：从 false→true 时自动聚焦标题输入框
        //    MilestoneEditPanel.IsVisible 绑定到 MilestoneEdit.IsVisible
        if (MilestoneEditPanel is not null)
        {
            MilestoneEditPanel.PropertyChanged += OnMilestoneEditPanelPropertyChanged;
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (MilestoneEditPanel is not null)
        {
            MilestoneEditPanel.PropertyChanged -= OnMilestoneEditPanelPropertyChanged;
        }
    }

    private void OnMilestoneEditPanelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // 监听 IsVisible 从 false→true 的变更，触发自动聚焦
        if (e.Property == IsVisibleProperty && e.NewValue is true)
        {
            DispatcherTimer.RunOnce(TryFocusMilestoneTitle, TimeSpan.FromMilliseconds(200));
        }
    }

    /// <summary>
    /// 自动聚焦标题输入框：弹层打开后延迟触发，确保控件已完全布局。
    /// </summary>
    private void TryFocusMilestoneTitle()
    {
        if (MilestoneTitleTextBox is null) return;
        // 仅当弹层可见时才聚焦（避免弹层已关闭时误触发）
        if (DataContext is GrowthViewModel vm && !vm.MilestoneEdit.IsVisible) return;
        try
        {
            if (MilestoneTitleTextBox.IsVisible)
            {
                MilestoneTitleTextBox.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
            }
        }
        catch { /* 聚焦失败时静默忽略，不影响主流程 */ }
    }

    public static readonly IValueConverter IsEditingConverter = new FuncValueConverter<string, bool>(
        s => s == "编辑成长时刻");

    private void OnAddMilestone(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is GrowthViewModel vm)
        {
            vm.AddMilestoneCommand.Execute(null);
        }
    }

    private void OnMilestoneTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && DataContext is GrowthViewModel vm)
        {
            if (border.DataContext is MilestoneDisplayItem item)
            {
                vm.EditMilestone(item);
                return;
            }
            if (border.Tag is MilestoneDisplayItem tagItem)
            {
                vm.EditMilestone(tagItem);
            }
        }
    }

    /// <summary>
    /// 点击"+"按钮：调用系统文件选择器选取图片。
    /// 对齐小程序：单选、支持 jpg/jpeg/png/gif/webp、sizeType=compressed 由 Avalonia 自动处理。
    /// </summary>
    private async void OnAddPhotoClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is not { } provider) return;

            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择照片",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("图片文件")
                    {
                        Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.webp" },
                        MimeTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" },
                    },
                },
            });

            if (files.Count > 0 && DataContext is GrowthViewModel vm)
            {
                await vm.MilestoneEdit.AddPhotoAsync(files[0]);
            }
        }
        catch (Exception ex)
        {
            ChildNotes.Infrastructure.DevLogger.Log("GrowthView", $"选择照片失败: {ex.Message}");
        }
    }

    /// <summary>关闭图片预览弹窗。</summary>
    private void OnClosePreview(object? sender, RoutedEventArgs e)
    {
        PhotoPreviewPanel.IsVisible = false;
        PreviewImage.Source = null;
    }

    /// <summary>
    /// 点击照片删除按钮：从 Tag 取出 MilestonePhotoItem，调用 VM 的 RemovePhotoCommand。
    /// 用 code-behind 而非 XAML 绑定，避免 DataTemplate 内跨级查找 DataContext 的复杂性。
    /// </summary>
    private void OnRemovePhotoClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MilestonePhotoItem item && DataContext is GrowthViewModel vm)
        {
            vm.MilestoneEdit.RemovePhotoCommand.Execute(item);
        }
    }
}
