using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;
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
        // ★ 订阅编辑弹层的可见性变化：触发抽屉滑入动画 + 自动聚焦
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
        if (e.Property == IsVisibleProperty)
        {
            if (e.NewValue is true)
            {
                // 打开抽屉：执行滑入动画
                _ = OnDrawerOpeningAsync();
                DispatcherTimer.RunOnce(TryFocusBabyName, TimeSpan.FromMilliseconds(300));
            }
            // 关闭时 IsVisible 已被绑定设为 false，瞬时隐藏（交互已完成，不影响体验）
        }
    }

    /// <summary>
    /// 底部抽屉入场动画：遮罩淡入 + 面板从底部滑入。
    /// 使用 Animation KeyFrame API，不依赖 Transitions。
    /// </summary>
    private async Task OnDrawerOpeningAsync()
    {
        if (EditorSheet == null || DrawerPanel == null) return;

        try
        {
            // 初始状态
            EditorSheet.Opacity = 0;
            DrawerPanel.RenderTransform = TransformOperations.Parse("translateY(100%)");

            // 等待一帧让布局完成
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            var duration = AnimationService.IsEnabled ? 250 : 1;

            // 并行执行：遮罩淡入 + 面板滑入
            var maskAnim = CreateFadeAnimation(0, 1, duration, new CubicEaseOut());
            var panelAnim = CreateSlideUpAnimation(duration, new CubicEaseOut());

            await Task.WhenAll(
                maskAnim.RunAsync(EditorSheet),
                panelAnim.RunAsync(DrawerPanel)
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"抽屉入场动画异常: {ex.Message}");
        }
    }

    /// <summary>创建淡入淡出动画。</summary>
    private static Animation CreateFadeAnimation(double from, double to, int durationMs, Easing easing)
    {
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = easing,
            FillMode = FillMode.Forward
        };

        var start = new KeyFrame { Cue = new Cue(0.0) };
        start.Setters.Add(new Setter(Visual.OpacityProperty, from));
        anim.Children.Add(start);

        var end = new KeyFrame { Cue = new Cue(1.0) };
        end.Setters.Add(new Setter(Visual.OpacityProperty, to));
        anim.Children.Add(end);

        return anim;
    }

    /// <summary>创建从底部滑入动画（translateY 100%→0）。</summary>
    private static Animation CreateSlideUpAnimation(int durationMs, Easing easing)
    {
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = easing,
            FillMode = FillMode.Forward
        };

        var start = new KeyFrame { Cue = new Cue(0.0) };
        start.Setters.Add(new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("translateY(100%)")));
        anim.Children.Add(start);

        var end = new KeyFrame { Cue = new Cue(1.0) };
        end.Setters.Add(new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("none")));
        anim.Children.Add(end);

        return anim;
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
