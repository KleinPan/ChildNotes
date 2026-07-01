using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class RecordSheetView : UserControl
{
    private const double DefaultMaxHeight = 600;

    // 上一次应用的键盘偏移量
    private double _lastKbOffset;

    public RecordSheetView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
        // 动态绑定疫苗 Grid 的 MaxWidth 到父容器宽度（自适应不同屏幕尺寸）
        ContentStackPanel.SizeChanged += OnContentStackSizeChanged;
    }

    /// <summary>
    /// 疫苗容器 Grid 的 * 列计算依赖有限宽度约束。
    /// ScrollViewer 给子元素无限宽度约束，导致 Grid DesiredSize 偏大。
    /// 通过动态设置 MaxWidth = ContentStackPanel.ActualWidth 来修复此问题，
    /// 同时保证在不同屏幕尺寸下自适应。
    /// </summary>
    private void OnContentStackSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (VaccineGrid is not null && e.NewSize.Width > 0)
        {
            VaccineGrid.MaxWidth = e.NewSize.Width;
        }
    }

    // 通用：非 null 转换器（用于 SelectedPlan 可见性）
    public static readonly IValueConverter IsNotNullConverter = new FuncValueConverter<object?, bool>(o => o is not null);

    // 疫苗折叠按钮文案：true→收起，false→+ 添加
    public static readonly IValueConverter VaccineToggleTextConverter = new FuncValueConverter<bool, string>(
        isExpanded => isExpanded ? "收起" : "+ 添加");

    // ===== 异常记录：呼吸道症状多选高亮转换器 =====
    // 呼吸道症状以中文文案为 key（与小程序 abnormal-form 选项一致），
    // 保存在 AbnormalForm.Respiratory 集合中。
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

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);
        AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);

        // ★ 使用 Avalonia 内置 InputPane API 监听键盘（跨平台，坐标系统一为 lp）
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.InputPane != null)
        {
            topLevel.InputPane.StateChanged += OnInputPaneStateChanged;
        }

        DevLogger.Log("SheetView", "Attached: listeners added");
    }

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.InputPane != null)
        {
            topLevel.InputPane.StateChanged -= OnInputPaneStateChanged;
        }

        DevLogger.Log("SheetView", "Detached: listeners removed");
    }

    /// <summary>★ 核心回调：Avalonia InputPane 键盘状态变化</summary>
    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs e)
    {
        if (SheetRoot is null || !IsVisible) return;

        if (e.NewState == InputPaneState.Closed)
        {
            ClearKeyboardOffset(reason: "InputPane.Closed");
            return;
        }

        if (e.NewState == InputPaneState.Open && e.EndRect.Height > 0)
        {
            ApplyKeyboardOffset(e.EndRect.Height, reason: "InputPane.Open");
        }
    }

    private void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox tb)
        {
            DispatcherTimer.RunOnce(() => ScrollFocusedIntoView(tb), TimeSpan.FromMilliseconds(100));
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        // 由 InputPane.Closed 统一处理回弹
    }

    /// <summary>应用键盘偏移（使用 InputPane 的 lp 坐标）</summary>
    private void ApplyKeyboardOffset(double keyboardHeightLp, string reason)
    {
        if (SheetRoot is null) return;

        var parent = SheetRoot.Parent as Layoutable;
        var containerHeight = parent?.Bounds.Height ?? 0;

        var maxOffset = containerHeight > 0 ? containerHeight * 0.9 : 800;
        var offset = Math.Min(keyboardHeightLp, maxOffset);

        SheetRoot.Margin = new Thickness(0, 0, 0, offset);

        if (containerHeight > 0)
        {
            SheetRoot.MaxHeight = Math.Max(280, containerHeight - offset);
        }

        _lastKbOffset = offset;

        DevLogger.Log("SheetView",
            $"ApplyOffset | {reason} | kbH={keyboardHeightLp:F1}lp | offset={offset:F1}lp | " +
            $"containerH={containerHeight:F1}lp | MaxH={SheetRoot.MaxHeight:F0} | " +
            $"sheetY={SheetRoot.Bounds.Y:F0} | sheetBottom={SheetRoot.Bounds.Y + SheetRoot.Bounds.Height:F0}");
    }

    /// <summary>清除键盘偏移</summary>
    private void ClearKeyboardOffset(string reason)
    {
        if (SheetRoot is null) return;
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
            tb.BringIntoView(new Avalonia.Rect(0, -12, bounds.Width, bounds.Height));
            DevLogger.Log("SheetView",
                $"BringIntoView | tb.Y={bounds.Y:F0} tb.H={bounds.Height:F0} | " +
                $"scrollOff={ContentScroll.Offset.Y:F0} sheetY={SheetRoot?.Bounds.Y:F0}");
        }
        catch (Exception ex)
        {
            DevLogger.Log("SheetView", $"BringIntoView failed: {ex.Message}");
        }
    }

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

    // ===== 疫苗时间轴相关 =====
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

    /// <summary>点击已打剂次的"改时间"按钮：弹出日期时间选择弹窗。</summary>
    private void OnVaccineEdit(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VaccinePlanView plan && DataContext is RecordSheetViewModel vm)
        {
            vm.RequestVaccineEdit(plan);
        }
    }

    /// <summary>点击已打剂次的"取消"按钮：弹出取消操作确认弹窗。</summary>
    private void OnVaccineCancelDone(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VaccinePlanView plan && DataContext is RecordSheetViewModel vm)
        {
            vm.RequestVaccineCancel(plan);
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
