using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;
using ChildNotes.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace ChildNotes.Views;

public partial class RecordSheetView : UserControl
{
    private const double DefaultMaxHeight = 600;

    private bool _textBoxFocused;
    private double _lastKbOffset;

    // ★ TabBar 高度（从 MainShellView 动态获取）
    private double _tabBarHeight;

    public RecordSheetView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
        ContentStackPanel.SizeChanged += OnContentStackSizeChanged;
    }

    private void OnContentStackSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (VaccineGrid is not null && e.NewSize.Width > 0)
        {
            VaccineGrid.MaxWidth = e.NewSize.Width;
        }
    }

    public static readonly IValueConverter IsNotNullConverter = new FuncValueConverter<object?, bool>(o => o is not null);
    public static readonly IValueConverter VaccineToggleTextConverter = new FuncValueConverter<bool, string>(
        isExpanded => isExpanded ? "收起" : "+ 添加");

    public static readonly IValueConverter RespiratoryContainsCough =
        new FuncValueConverter<ObservableCollection<string>, bool>(c => c is not null && c.Contains("咳嗽(轻微)"));
    public static readonly IValueConverter RespiratoryContainsCoughFreq =
        new FuncValueConverter<ObservableCollection<string>, bool>(c => c is not null && c.Contains("咳嗽(频繁)"));
    public static readonly IValueConverter RespiratoryContainsSneeze =
        new FuncValueConverter<ObservableCollection<string>, bool>(c => c is not null && c.Contains("打喷嚏"));
    public static readonly IValueConverter RespiratoryContainsRhinorrhea =
        new FuncValueConverter<ObservableCollection<string>, bool>(c => c is not null && c.Contains("流鼻涕"));
    public static readonly IValueConverter RespiratoryContainsCongestion =
        new FuncValueConverter<ObservableCollection<string>, bool>(c => c is not null && c.Contains("鼻塞"));
    public static readonly IValueConverter RespiratoryContainsTachypnea =
        new FuncValueConverter<ObservableCollection<string>, bool>(c => c is not null && c.Contains("呼吸急促"));

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);
        AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
        KeyboardHeightProvider.HeightChanged += OnKeyboardHeightChanged;

        UpdateTabBarHeight();

        DevLogger.Log("SheetView", "Attached: listeners added");
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
        KeyboardHeightProvider.HeightChanged -= OnKeyboardHeightChanged;
        DevLogger.Log("SheetView", "Detached: listeners removed");
    }

    /// <summary>
    /// 监听 IsVisible 变化：
    /// - 显示时：执行底部滑入入场动画 + 延迟聚焦输入框
    /// - 隐藏时：清除动画残留状态
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
        {
            if (change.NewValue is true)
            {
                _ = PlaySheetEnterAnimationAsync();
                DispatcherTimer.RunOnce(TryFocusPrimaryTextBox, TimeSpan.FromMilliseconds(350));
            }
            else if (change.OldValue is true)
            {
                // 隐藏时清除动画可能残留的 RenderTransform，避免影响下次键盘偏移逻辑
                if (SheetRoot is not null && _lastKbOffset == 0)
                {
                    SheetRoot.RenderTransform = null;
                }
            }
        }
    }

    /// <summary>
    /// 底部功能卡片入场动画：遮罩淡入 + 面板从底部滑入。
    /// 使用 Animation KeyFrame API，动画完成后显式清除 RenderTransform，
    /// 避免与键盘偏移逻辑（也操作 SheetRoot.RenderTransform）冲突。
    /// </summary>
    private async Task PlaySheetEnterAnimationAsync()
    {
        if (SheetRoot is null) return;

        try
        {
            // 动画关闭时：直接显示，跳过动画
            if (!AnimationService.IsEnabled)
            {
                SheetRoot.RenderTransform = null;
                if (SheetMask is not null) SheetMask.Opacity = 1;
                return;
            }

            // 初始状态：面板在屏幕底部之外，遮罩透明
            SheetRoot.RenderTransform = TransformOperations.Parse("translateY(100%)");
            if (SheetMask is not null) SheetMask.Opacity = 0;

            // 等待一帧让布局完成
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            // 并行执行：遮罩淡入（250ms）+ 面板滑入（350ms CubicEaseOut 自然减速）
            var maskAnim = CreateMaskFadeAnimation(0, 1, 250, new CubicEaseOut());
            var slideAnim = CreateSlideUpAnimation(350, new CubicEaseOut());

            var tasks = new System.Collections.Generic.List<Task>();
            if (SheetMask is not null) tasks.Add(maskAnim.RunAsync(SheetMask));
            tasks.Add(slideAnim.RunAsync(SheetRoot));

            await Task.WhenAll(tasks);

            // ★ 动画完成后：清除 RenderTransform 让键盘偏移逻辑接管，遮罩设为完全不透明
            SheetRoot.RenderTransform = null;
            if (SheetMask is not null) SheetMask.Opacity = 1;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"功能卡片入场动画异常: {ex.Message}");
            // 异常时确保面板正常显示
            SheetRoot.RenderTransform = null;
            if (SheetMask is not null) SheetMask.Opacity = 1;
        }
    }

    /// <summary>创建遮罩淡入淡出动画。</summary>
    private static Animation CreateMaskFadeAnimation(double from, double to, int durationMs, Easing easing)
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
        start.Setters.Add(new Setter(Visual.RenderTransformProperty,
            TransformOperations.Parse("translateY(100%)")));
        anim.Children.Add(start);
        var end = new KeyFrame { Cue = new Cue(1.0) };
        end.Setters.Add(new Setter(Visual.RenderTransformProperty,
            TransformOperations.Parse("none")));
        anim.Children.Add(end);
        return anim;
    }

    /// <summary>
    /// 自动聚焦当前记录类型的主输入框：弹窗打开后延迟触发，确保控件已完全布局。
    /// 延迟 200ms 是为了等待表单切换 IsVisible 后的布局刷新（综合表单比单输入框场景更复杂）。
    /// 
    /// 焦点选择策略（按记录类型）：
    /// - feed: 母乳→左侧时长；瓶喂/吸出→奶量
    /// - diaper: 备注
    /// - sleep: 无主输入框（用 TimePicker），不聚焦
    /// - temperature: 体温
    /// - growth: 身高
    /// - supplement: 剂量
    /// - pump: 左侧时长
    /// - complementary: 食量
    /// - abnormal: 体温
    /// - activity: 活动名称
    /// - vaccine: 无主输入框（按钮操作为主），不聚焦
    /// </summary>
    private void TryFocusPrimaryTextBox()
    {
        if (!IsVisible) return;
        if (DataContext is not RecordSheetViewModel vm) return;

        TextBox? target = ResolvePrimaryTextBox(vm.ActiveType, vm);
        if (target is null) return;

        // 目标 TextBox 可能在条件 StackPanel 内（IsVisible 绑定），需等待其可见
        if (!target.IsVisible)
        {
            // 二次延迟：等待条件容器可见后再聚焦
            DispatcherTimer.RunOnce(() =>
            {
                if (IsVisible && target.IsVisible)
                {
                    target.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
                    DevLogger.Log("SheetView", $"AutoFocus (deferred): type={vm.ActiveType}");
                }
            }, TimeSpan.FromMilliseconds(150));
            return;
        }

        try
        {
            target.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
            DevLogger.Log("SheetView", $"AutoFocus: type={vm.ActiveType}");
        }
        catch (Exception ex)
        {
            DevLogger.Log("SheetView", $"AutoFocus failed: {ex.Message}");
        }
    }

    /// <summary>根据当前记录类型解析主输入框。</summary>
    private TextBox? ResolvePrimaryTextBox(string activeType, RecordSheetViewModel vm)
    {
        return activeType switch
        {
            RecordType.Feed => vm.FeedForm.FeedType == FeedType.Breast
                ? FeedLeftDurationTextBox
                : FeedAmountTextBox,
            RecordType.Diaper => DiaperNoteTextBox,
            RecordType.Temperature => TemperatureTextBox,
            RecordType.Growth => GrowthHeightTextBox,
            RecordType.Supplement => SupplementDoseTextBox,
            RecordType.Pump => PumpLeftDurationTextBox,
            RecordType.Complementary => ComplementaryAmountTextBox,
            RecordType.Abnormal => AbnormalTemperatureTextBox,
            RecordType.Activity => ActivityNameTextBox,
            // sleep / vaccine / milestone 等：无主输入框，不自动聚焦
            _ => null,
        };
    }

    /// <summary>向上查找 MainShellView 并获取其 TabBar 高度</summary>
    private void UpdateTabBarHeight()
    {
        _tabBarHeight = 0;
        try
        {
            var shell = this.FindAncestorOfType<MainShellView>();
            if (shell is not null)
            {
                var tabBar = shell.GetVisualDescendants()
                    .FirstOrDefault(c => c is Border b && b.Classes.Contains("tab-bar")) as Border;
                if (tabBar?.Bounds.Height > 0)
                {
                    _tabBarHeight = tabBar.Bounds.Height;
                    DevLogger.Log("SheetView", $"TabBar height: {_tabBarHeight:F1}lp");
                }
            }
        }
        catch { /* 查找失败时保持为0 */ }
    }

    private void OnKeyboardHeightChanged(double keyboardHeightLp)
    {
        if (!OperatingSystem.IsAndroid()) return;

        // ★ 键盘收回：立即清除偏移让卡片回弹
        if (keyboardHeightLp <= 0 && _lastKbOffset > 0 && IsVisible)
        {
            ClearKeyboardOffset(reason: "keyboard dismissed (native callback)");
            return;
        }

        if (IsVisible)
        {
            ApplyKeyboardOffset(reason: $"NativeKeyboard height={keyboardHeightLp:F0}lp");
        }
    }

    private void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox tb)
        {
            _textBoxFocused = true;
            ApplyKeyboardOffset(reason: $"TextBox focused");
            DispatcherTimer.RunOnce(() => ScrollFocusedIntoView(tb), TimeSpan.FromMilliseconds(100));
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox)
        {
            _textBoxFocused = false;
            DispatcherTimer.RunOnce(() =>
            {
                if (!_textBoxFocused && _lastKbOffset > 0)
                {
                    ClearKeyboardOffset(reason: "TextBox lost focus (deferred)");
                }
            }, TimeSpan.FromMilliseconds(200));
        }
    }

    private void ApplyKeyboardOffset(string reason)
    {
        if (SheetRoot is null) return;
        if (!OperatingSystem.IsAndroid()) return;

        var kbHeight = KeyboardHeightProvider.CurrentHeight;
        var parent = SheetRoot.Parent as Layoutable;
        var containerHeight = parent?.Bounds.Height ?? 0;

        double offset;
        string offsetSource;
        if (kbHeight > 10)
        {
            // ★ 扣除 TabBar 高度
            offset = Math.Max(0, kbHeight - _tabBarHeight);
            offsetSource = "native";
        }
        else if (_textBoxFocused)
        {
            offset = containerHeight > 0 ? containerHeight * 0.45 : 350;
            offsetSource = "fallback";
        }
        else
        {
            if (_lastKbOffset > 0)
            {
                ClearKeyboardOffset(reason: $"keyboard dismissed ({reason})");
            }
            return;
        }

        var maxOffset = containerHeight > 0 ? containerHeight * 0.92 : 800;
        if (offset > maxOffset)
        {
            offset = maxOffset;
            offsetSource += "(clamped)";
        }

        // TranslateTransform：纯视觉偏移
        SheetRoot.RenderTransform = new TranslateTransform(0, -offset);
        SheetRoot.Margin = new Thickness(0);

        if (containerHeight > 0)
        {
            SheetRoot.MaxHeight = Math.Max(280, containerHeight - offset);
        }

        DevLogger.Log("SheetView",
            $"ApplyOffset | {reason} | src={offsetSource} | kbH={kbHeight:F1}lp | tabBarH={_tabBarHeight:F1}lp | offset={offset:F1}lp | " +
            $"containerH={containerHeight:F1}lp | MaxH={SheetRoot.MaxHeight:F0} | " +
            $"sheetY={SheetRoot.Bounds.Y:F0} | sheetBottom={SheetRoot.Bounds.Y + SheetRoot.Bounds.Height:F0}");
        _lastKbOffset = offset;
    }

    private void ClearKeyboardOffset(string reason)
    {
        if (SheetRoot is null) return;
        SheetRoot.RenderTransform = null;
        SheetRoot.Margin = new Thickness(0);
        SheetRoot.MaxHeight = DefaultMaxHeight;
        _lastKbOffset = 0;
        DevLogger.Log("SheetView", $"ClearOffset | {reason}");
    }

    private void ScrollFocusedIntoView(TextBox tb)
    {
        try
        {
            if (ContentScroll is null) return;
            var bounds = tb.Bounds;
            tb.BringIntoView(new Rect(0, -12, bounds.Width, bounds.Height));
        }
        catch (Exception ex)
        {
            DevLogger.Log("SheetView", $"BringIntoView failed: {ex.Message}");
        }
    }

    // ===== 喂养/尿布/补充剂等快捷按钮 =====
    private void OnFeedBottle(object sender, PointerPressedEventArgs e) => SwitchFeed("bottle");
    private void OnFeedBreast(object sender, PointerPressedEventArgs e) => SwitchFeed("breast");
    private void OnFeedExpressed(object sender, PointerPressedEventArgs e) => SwitchFeed("expressed");
    private void SwitchFeed(string t) { if (DataContext is RecordSheetViewModel vm) vm.FeedForm.SwitchType(t); }

    private void OnDiaperWet(object sender, PointerPressedEventArgs e) => SwitchDiaper("wet");
    private void OnDiaperDirty(object sender, PointerPressedEventArgs e) => SwitchDiaper("dirty");
    private void OnDiaperBoth(object sender, PointerPressedEventArgs e) => SwitchDiaper("both");
    private void OnDiaperDry(object sender, PointerPressedEventArgs e) => SwitchDiaper("dry");
    private void SwitchDiaper(string t) { if (DataContext is RecordSheetViewModel vm) vm.DiaperForm.SelectType(t); }

    private void OnSuppSupplement(object sender, PointerPressedEventArgs e) => SwitchSupp("supplement");
    private void OnSuppMedicine(object sender, PointerPressedEventArgs e) => SwitchSupp("medicine");
    private void OnSuppNutrition(object sender, PointerPressedEventArgs e) => SwitchSupp("nutrition");
    private void SwitchSupp(string t) { if (DataContext is RecordSheetViewModel vm) vm.SupplementForm.SwitchType(t); }

    /// <summary>点击 Chip 单选：先清空同列表其他项，再切换当前项（允许取消选中）。</summary>
    private void OnSuppChipTap(object? sender, TappedEventArgs e)
    {
        if (sender is Border { Tag: CommonItemViewModel item } && DataContext is RecordSheetViewModel vm)
        {
            // 单选：除当前项外，清空其余所有项的选中态
            foreach (var other in vm.SupplementForm.CurrentAllItems)
            {
                if (!ReferenceEquals(other, item)) other.IsSelected = false;
            }
            item.IsSelected = !item.IsSelected;
        }
    }

    /// <summary>右键点击自定义 Chip 弹出删除确认对话框（桌面端右键 = 移动端长按的等效操作）。</summary>
    private async void OnSuppChipPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Border { Tag: CommonItemViewModel item }) return;
        if (!item.IsCustom) return;  // 仅自定义项可删除
        // 仅响应右键，左键由 Tapped 处理
        var point = e.GetCurrentPoint((Visual)sender);
        if (!point.Properties.IsRightButtonPressed) return;
        if (DataContext is not RecordSheetViewModel vm) return;

        e.Handled = true;
        var confirmed = await ShowConfirmDialog("删除自定义项", $"确定删除「{item.Name}」吗？");
        if (confirmed)
        {
            vm.SupplementForm.DeleteCustomCommand.Execute(item);
        }
    }

    /// <summary>点击单位 Chip 单选：先清空其他单位选中，再切换当前项（允许取消选中）。</summary>
    private void OnUnitChipTap(object? sender, TappedEventArgs e)
    {
        if (sender is Border { Tag: CommonItemViewModel item } && DataContext is RecordSheetViewModel vm)
        {
            foreach (var other in vm.SupplementForm.AllDoseUnitItems)
            {
                if (!ReferenceEquals(other, item)) other.IsSelected = false;
            }
            item.IsSelected = !item.IsSelected;
        }
    }

    /// <summary>辅食单位 Chip 单选：清空其他单位选中，切换当前项。</summary>
    private void OnCompUnitChipTap(object? sender, TappedEventArgs e)
    {
        if (sender is Border { Tag: CommonItemViewModel item } && DataContext is RecordSheetViewModel vm)
        {
            foreach (var other in vm.ComplementaryForm.AmountUnitItems)
            {
                if (!ReferenceEquals(other, item)) other.IsSelected = false;
            }
            item.IsSelected = !item.IsSelected;
        }
    }

    /// <summary>辅食食物 Chip 多选：切换当前项选中状态（不影响其他项）。</summary>
    private void OnCompFoodChipTap(object? sender, TappedEventArgs e)
    {
        if (sender is Border { Tag: CommonItemViewModel item } && DataContext is RecordSheetViewModel vm)
        {
            item.IsSelected = !item.IsSelected;
        }
    }

    /// <summary>右键点击自定义辅食 Chip 弹出删除确认对话框（桌面端右键 = 移动端长按的等效操作）。</summary>
    private async void OnCompFoodChipPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Border { Tag: CommonItemViewModel item }) return;
        if (!item.IsCustom) return;
        var point = e.GetCurrentPoint((Visual)sender);
        if (!point.Properties.IsRightButtonPressed) return;
        if (DataContext is not RecordSheetViewModel vm) return;

        e.Handled = true;
        var confirmed = await ShowConfirmDialog("删除自定义辅食", $"确定删除「{item.Name}」吗？");
        if (confirmed)
        {
            vm.ComplementaryForm.DeleteCustomCommand.Execute(item);
        }
    }

    /// <summary>右键点击自定义单位 Chip 弹出删除确认对话框。</summary>
    private async void OnUnitChipPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Border { Tag: CommonItemViewModel item }) return;
        if (!item.IsCustom) return;
        var point = e.GetCurrentPoint((Visual)sender);
        if (!point.Properties.IsRightButtonPressed) return;
        if (DataContext is not RecordSheetViewModel vm) return;

        e.Handled = true;
        var confirmed = await ShowConfirmDialog("删除自定义单位", $"确定删除单位「{item.Name}」吗？");
        if (confirmed)
        {
            vm.SupplementForm.DeleteCustomUnitCommand.Execute(item);
        }
    }

    /// <summary>简单的确认对话框（用独立 Window 实现模态弹窗）。</summary>
    private Task<bool> ShowConfirmDialog(string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        Window? dialog = null;
        dialog = new Window
        {
            Title = title,
            Width = 300,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 20), TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children =
                        {
                            new Button
                            {
                                Content = "取消",
                                Margin = new Thickness(0, 0, 8, 0),
                                Command = new RelayCommand(() => { tcs.TrySetResult(false); dialog?.Close(); }),
                            },
                            new Button
                            {
                                Content = "删除",
                                Background = Brushes.OrangeRed,
                                Foreground = Brushes.White,
                                Command = new RelayCommand(() => { tcs.TrySetResult(true); dialog?.Close(); }),
                            },
                        },
                    },
                },
            },
        };
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
        return tcs.Task;
    }

    private void OnTexturePuree(object sender, PointerPressedEventArgs e) => SwitchTexture("puree");
    private void OnTextureMashed(object sender, PointerPressedEventArgs e) => SwitchTexture("mashed");
    private void OnTextureLumpy(object sender, PointerPressedEventArgs e) => SwitchTexture("lumpy");
    private void SwitchTexture(string t) { if (DataContext is RecordSheetViewModel vm) vm.ComplementaryForm.SelectTexture(t); }

    private void OnReactionNone(object sender, PointerPressedEventArgs e) => SwitchReaction("none");
    private void OnReactionAllergy(object sender, PointerPressedEventArgs e) => SwitchReaction("allergy");
    private void OnReactionVomit(object sender, PointerPressedEventArgs e) => SwitchReaction("vomit");
    private void OnReactionDiarrhea(object sender, PointerPressedEventArgs e) => SwitchReaction("diarrhea");
    private void SwitchReaction(string r) { if (DataContext is RecordSheetViewModel vm) vm.ComplementaryForm.SelectReaction(r); }

    private void OnCategoryPlay(object sender, PointerPressedEventArgs e) => SwitchCategory("play");
    private void OnCategoryOutdoor(object sender, PointerPressedEventArgs e) => SwitchCategory("outdoor");
    private void OnCategoryExercise(object sender, PointerPressedEventArgs e) => SwitchCategory("exercise");
    private void SwitchCategory(string c) { if (DataContext is RecordSheetViewModel vm) vm.ActivityForm.SelectCategory(c); }

    // ===== 疫苗相关 =====
    private void OnVaccineDoseClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VaccinePlanView plan && DataContext is RecordSheetViewModel vm)
        {
            vm.VaccineForm.SelectDose(plan);
        }
    }

    private async void OnVaccineMarkDone(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VaccinePlanView plan && DataContext is RecordSheetViewModel vm)
        {
            await vm.MarkVaccineDoneAsync(plan);
        }
    }

    private async void OnVaccineSkip(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VaccinePlanView plan && DataContext is RecordSheetViewModel vm)
        {
            await vm.MarkVaccineSkippedAsync(plan);
        }
    }

    private void OnVaccineEdit(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VaccinePlanView plan && DataContext is RecordSheetViewModel vm)
        {
            vm.RequestVaccineEdit(plan);
        }
    }

    private void OnVaccineCancelDone(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VaccinePlanView plan && DataContext is RecordSheetViewModel vm)
        {
            vm.RequestVaccineCancel(plan);
        }
    }

    /// <summary>已跳过剂次的直接取消（不弹窗，与跳过操作对称）。</summary>
    private void OnVaccineCancelSkipped(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VaccinePlanView plan && DataContext is RecordSheetViewModel vm)
        {
            vm.CancelVaccineSkippedDirect(plan);
        }
    }

    private void OnVaccineToggleCustomForm(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecordSheetViewModel vm)
        {
            vm.VaccineForm.ToggleCustomVaccineFormCommand.Execute(null);
        }
    }

    private void OnCustomCategoryFree(object sender, PointerPressedEventArgs e) => SwitchCustomCategory("free");
    private void OnCustomCategoryPaid(object sender, PointerPressedEventArgs e) => SwitchCustomCategory("paid");
    private void SwitchCustomCategory(string c)
    {
        if (DataContext is RecordSheetViewModel vm) vm.VaccineForm.SwitchCustomCategoryCommand.Execute(c);
    }

    private async void OnAddCustomVaccine(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecordSheetViewModel vm)
        {
            await vm.AddCustomVaccineAsync();
        }
    }

    // ===== 异常记录：呼吸道症状多选 =====
    private void OnRespCough(object sender, PointerPressedEventArgs e) => ToggleResp("咳嗽(轻微)");
    private void OnRespCoughFreq(object sender, PointerPressedEventArgs e) => ToggleResp("咳嗽(频繁)");
    private void OnRespSneeze(object sender, PointerPressedEventArgs e) => ToggleResp("打喷嚏");
    private void OnRespRhinorrhea(object sender, PointerPressedEventArgs e) => ToggleResp("流鼻涕");
    private void OnRespCongestion(object sender, PointerPressedEventArgs e) => ToggleResp("鼻塞");
    private void OnRespTachypnea(object sender, PointerPressedEventArgs e) => ToggleResp("呼吸急促");
    private void ToggleResp(string symptom)
    {
        if (DataContext is RecordSheetViewModel vm) vm.AbnormalForm.ToggleRespiratory(symptom);
    }

    // ===== 异常记录：呕吐类型单选 =====
    private void OnVomitSpitUp(object sender, PointerPressedEventArgs e) => SelectVomit("溢奶");
    private void OnVomitProjectile(object sender, PointerPressedEventArgs e) => SelectVomit("喷射");
    private void SelectVomit(string type)
    {
        if (DataContext is RecordSheetViewModel vm) vm.AbnormalForm.SelectVomit(type);
    }
}
