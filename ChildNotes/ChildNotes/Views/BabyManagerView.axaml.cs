using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.ViewModels;
using System.Globalization;

namespace ChildNotes.Views;

public partial class BabyManagerView : UserControl
{
    public BabyManagerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // ★ 订阅编辑弹层的可见性变化：从 false→true 时自动聚焦姓名输入框
        //    EditorSheet.IsVisible 绑定到 IsEditorOpen
        if (EditorSheet is not null)
        {
            EditorSheet.PropertyChanged += OnEditorSheetPropertyChanged;
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (EditorSheet is not null)
        {
            EditorSheet.PropertyChanged -= OnEditorSheetPropertyChanged;
        }
    }

    private void OnEditorSheetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // 监听 IsVisible 变更，切换 open class 触发 Transitions 动画
        if (e.Property == IsVisibleProperty)
        {
            if (e.NewValue is true)
            {
                // 打开抽屉：容器已显示，下一帧添加 open class 触发入场动画
                Dispatcher.UIThread.Post(() =>
                {
                    EditorSheet?.Classes.Add("open");
                    DrawerPanel?.Classes.Add("open");
                });
                DispatcherTimer.RunOnce(TryFocusBabyName, TimeSpan.FromMilliseconds(300));
            }
            else
            {
                // 关闭抽屉：移除 open class 触发退场动画
                // 注意：IsVisible 已被绑定设为 false，容器会立即隐藏
                // 但由于关闭前用户已点击按钮（交互完成），瞬时隐藏不影响体验
                EditorSheet?.Classes.Remove("open");
                DrawerPanel?.Classes.Remove("open");
            }
        }
    }

    /// <summary>
    /// 自动聚焦姓名输入框：弹层打开后延迟触发，确保控件已完全布局。
    /// </summary>
    private void TryFocusBabyName()
    {
        if (BabyNameTextBox is null) return;
        if (Vm is not { IsEditorOpen: true }) return;
        try
        {
            if (BabyNameTextBox.IsVisible)
            {
                BabyNameTextBox.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
            }
        }
        catch { /* 聚焦失败时静默忽略，不影响主流程 */ }
    }

    // 判断是否为当前宝宝（参数: Baby, 用 AppState.CurrentBaby.Id 比较）
    public static readonly IValueConverter IsCurrentBabyConverter = new IsCurrentBabyConverter();

    private BabyManagerViewModel? Vm => DataContext as BabyManagerViewModel;

    private void OnBabyItemTapped(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is Baby baby && Vm is { } vm)
        {
            vm.OpenEdit(baby);
        }
    }

    private void OnAddTapped(object? sender, RoutedEventArgs e) => Vm?.OpenAdd();

    private void OnBoyTap(object? sender, RoutedEventArgs e) => Vm?.SelectGender("boy");
    private void OnGirlTap(object? sender, RoutedEventArgs e) => Vm?.SelectGender("girl");

    /// <summary>
    /// 头像点击：调用系统文件选择器选取图片。
    /// 桌面端用 StorageProvider.OpenFilePicker；安卓端通过 Avalonia 的文件选择 API。
    /// </summary>
    private async void OnAvatarTap(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is not { } provider) return;

            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择头像",
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

            if (files.Count > 0 && Vm is { } vm)
            {
                await vm.LoadAvatarFromFile(files[0]);
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("BabyManagerView", $"选择头像失败: {ex.Message}");
        }
    }

    private void OnDeleteTapped(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { IsEditing: true } vm) return;
        var editing = ServiceProvider.Instance.AppState.BabyList.FirstOrDefault(b => b.Id == vm.EditingId);
        if (editing is not null) vm.OpenDeleteConfirm(editing);
    }
}

file sealed class IsCurrentBabyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Baby baby)
        {
            return ServiceProvider.Instance.AppState.CurrentBaby?.Id == baby.Id;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
