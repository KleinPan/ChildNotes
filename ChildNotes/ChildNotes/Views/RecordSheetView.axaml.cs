using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class RecordSheetView : UserControl
{
    private const double DefaultMaxHeight = 600;

    // 当前是否有 TextBox 处于焦点状态（用于判断是否要保持键盘规避）
    private bool _textBoxFocused;

    public RecordSheetView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
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
        // 监听整个控件的 GotFocus/LostFocus（冒泡），判断焦点是否落在 TextBox 上
        AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);
        AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
        // 订阅安卓原生键盘高度变化事件（由 ViewTreeObserver 驱动）
        KeyboardHeightProvider.HeightChanged += OnKeyboardHeightChanged;
        DevLogger.Log("SheetView", "Attached: GotFocus/LostFocus/KeyboardHeight listeners added");
    }

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
        KeyboardHeightProvider.HeightChanged -= OnKeyboardHeightChanged;
        DevLogger.Log("SheetView", "Detached: listeners removed");
    }

    /// <summary>
    /// 安卓原生键盘高度变化回调。
    /// 当 ViewTreeObserver 检测到键盘弹出/收起时触发，
    /// 使用真实键盘高度重新计算抽屉 MaxHeight。
    /// </summary>
    private void OnKeyboardHeightChanged(double keyboardHeightLp)
    {
        // 仅在 TextBox 持有焦点时响应键盘高度变化
        if (_textBoxFocused && IsVisible)
        {
            RecalcMaxHeight(reason: $"NativeKeyboard height={keyboardHeightLp:F0}lp");
        }
        DevLogger.Log("SheetView", $"OnKeyboardHeightChanged | height={keyboardHeightLp:F0}lp | focused={_textBoxFocused}");
    }

    private void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        // 仅当焦点进入 TextBox/TimePicker 等可编辑控件时，认为软键盘可能弹出
        if (e.Source is TextBox tb)
        {
            _textBoxFocused = true;
            RecalcMaxHeight(reason: $"TextBox focused: {(tb.Name ?? tb.Text ?? "<empty>")}");
            // 焦点变化后给 ScrollViewer 一帧时间重算，再滚动到当前 TextBox
            DispatcherTimer.RunOnce(() => ScrollFocusedIntoView(tb), TimeSpan.FromMilliseconds(80));
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox)
        {
            // 失焦时延迟 150ms 检查，给下一个 TextBox 获得焦点留出时间。
            // 若 150ms 内新的 GotFocus 触发（_textBoxFocused 重新置 true），则不收缩。
            _textBoxFocused = false;
            DispatcherTimer.RunOnce(() =>
            {
                if (!_textBoxFocused)
                {
                    RestoreDefaultMaxHeight(reason: "TextBox lost focus (deferred)");
                }
            }, TimeSpan.FromMilliseconds(150));
        }
    }

    /// <summary>
    /// 根据安卓原生键盘高度 + 屏幕可用高度，重新计算抽屉 MaxHeight。
    /// 优先使用 <see cref="KeyboardHeightProvider.CurrentHeight"/>（安卓原生值）；
    /// 若尚未收到原生回调（桌面端或键盘还没弹起），回退到屏幕高度的 50% 作为保守估计。
    /// </summary>
    private void RecalcMaxHeight(string reason)
    {
        if (SheetRoot is null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var viewHeight = topLevel.ClientSize.Height;
        if (viewHeight <= 0) return;

        double newMax;
        var kbHeight = KeyboardHeightProvider.CurrentHeight;

        if (kbHeight > 0)
        {
            // 有原生键盘高度：屏幕总高 - 键盘高度 - 少量底部间距
            newMax = Math.Max(280, viewHeight - kbHeight - 16);
        }
        else
        {
            // 无原生数据（桌面端 或 键盘还未弹起）：保守估计键盘占 50%
            newMax = Math.Max(280, viewHeight * 0.50);
        }

        var oldMax = SheetRoot.MaxHeight;
        SheetRoot.MaxHeight = newMax;

        DevLogger.Log("SheetView",
            $"RecalcMaxHeight | {reason} | viewH={viewHeight:F0} | kbH={kbHeight:F0}lp | " +
            $"MaxHeight {oldMax:F0}->{newMax:F0} | " +
            $"Ext={ContentScroll?.Extent.Height:F0} Vp={ContentScroll?.Viewport.Height:F0} Off={ContentScroll?.Offset.Y:F0}");
    }

    /// <summary>恢复默认 MaxHeight。</summary>
    private void RestoreDefaultMaxHeight(string reason)
    {
        if (SheetRoot is null) return;
        var oldMax = SheetRoot.MaxHeight;
        SheetRoot.MaxHeight = DefaultMaxHeight;
        DevLogger.Log("SheetView", $"RestoreDefault | {reason} | MaxHeight {oldMax:F0}->{DefaultMaxHeight}");
    }

    /// <summary>
    /// 把当前聚焦的 TextBox 滚动到 ScrollViewer 可视区内。
    /// 键盘弹出后即使 MaxHeight 收缩，聚焦的输入框也可能在可视区下方，
    /// 必须显式调用 BringIntoView 才能确保不被键盘遮住。
    /// </summary>
    private void ScrollFocusedIntoView(TextBox tb)
    {
        try
        {
            if (ContentScroll is null) return;
            // 让 TextBox 顶部偏上一点，避免紧贴键盘上沿
            var bounds = tb.Bounds;
            tb.BringIntoView(new Avalonia.Rect(0, -8, bounds.Width, bounds.Height));
            DevLogger.Log("SheetView", $"BringIntoView | tb.Bounds={tb.Bounds.Y:F0},{tb.Bounds.Height:F0} | ScrollOffset={ContentScroll.Offset.Y:F0}");
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

    private void OnVaccineMarkDone(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VaccinePlanView plan && DataContext is RecordSheetViewModel vm)
        {
            vm.MarkVaccineDone(plan);
        }
    }

    private void OnVaccineSkip(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VaccinePlanView plan && DataContext is RecordSheetViewModel vm)
        {
            vm.MarkVaccineSkipped(plan);
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

    private void OnAddCustomVaccine(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecordSheetViewModel vm)
        {
            vm.AddCustomVaccine();
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
