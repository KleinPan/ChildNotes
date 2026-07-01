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
    // 上一次应用的键盘偏移量（用于日志对比）
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

    private void OnKeyboardHeightChanged(double keyboardHeightLp)
    {
        // 桌面端不会触发此回调（KeyboardHeightService 只在安卓端注册），但防御性判断
        if (!OperatingSystem.IsAndroid()) return;

        // 无论是否 focused 都响应——用户可能在键盘已弹出时切换表单类型
        if (IsVisible)
        {
            ApplyKeyboardOffset(reason: $"NativeKeyboard height={keyboardHeightLp:F0}lp");
        }
        DevLogger.Log("SheetView", $"OnKeyboardHeightChanged | height={keyboardHeightLp:F0}lp | visible={IsVisible}");
    }

    private void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox tb)
        {
            _textBoxFocused = true;
            ApplyKeyboardOffset(reason: $"TextBox focused: {(tb.Name ?? tb.Text ?? "<empty>")}");
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
                if (!_textBoxFocused)
                {
                    ClearKeyboardOffset(reason: "TextBox lost focus (deferred)");
                }
            }, TimeSpan.FromMilliseconds(200));
        }
    }

    /// <summary>
    /// 核心修复：通过设置 SheetRoot 的 Margin.Bottom 将整个抽屉向上推移，
    /// 避开软键盘遮挡。同时限制 MaxHeight 作为安全网（防止长表单溢出到键盘区域）。
    ///
    /// 关键修正：使用 SheetRoot 父容器的 Bounds.Height 作为可用高度基准，
    /// 而非 TopLevel.ClientSize.Height（后者包含 TabBar 等非内容区域）。
    /// </summary>
    private void ApplyKeyboardOffset(string reason)
    {
        if (SheetRoot is null)
        {
            DevLogger.Log("SheetView", "ApplyOffset SKIP: SheetRoot is null");
            return;
        }

        // 桌面端无软键盘，跳过偏移逻辑
        if (!OperatingSystem.IsAndroid())
        {
            DevLogger.Log("SheetView", $"ApplyOffset SKIP: not Android (reason={reason})");
            return;
        }

        var kbHeight = KeyboardHeightProvider.CurrentHeight;

        // ★ 关键：使用父容器实际高度而非窗口高度
        var parent = SheetRoot.Parent as Avalonia.Layout.Layoutable;
        var containerHeight = parent?.Bounds.Height ?? 0;

        // 窗口高度仅用于日志对比
        var topLevel = TopLevel.GetTopLevel(this);
        var windowHeight = topLevel?.ClientSize.Height ?? 0;

        double offset;
        string offsetSource;
        if (kbHeight > 10)
        {
            offset = kbHeight;
            offsetSource = "native";
        }
        else if (_textBoxFocused)
        {
            offset = containerHeight > 0 ? containerHeight * 0.45 : 350;
            offsetSource = "fallback";
        }
        else
        {
            DevLogger.Log("SheetView", $"ApplyOffset SKIP: no keyboard & no focus (reason={reason})");
            return;
        }

        // 安全上限
        var maxOffset = containerHeight > 0 ? containerHeight * 0.92 : 800;
        var clamped = false;
        if (offset > maxOffset)
        {
            offset = maxOffset;
            offsetSource += "(clamped)";
            clamped = true;
        }

        SheetRoot.Margin = new Thickness(0, 0, 0, offset);

        if (containerHeight > 0)
        {
            SheetRoot.MaxHeight = Math.Max(280, containerHeight - offset);
        }

        DevLogger.Log("SheetView",
            $"ApplyOffset | {reason} | src={offsetSource} | " +
            $"kbH={kbHeight:F1}lp | offset={offset:F1}lp | " +
            $"containerH={containerHeight:F1}lp | winH={windowHeight:F1}lp | clamped={clamped} | " +
            $"MaxH={SheetRoot.MaxHeight:F0} | margin={SheetRoot.Margin.Bottom:F0} | " +
            $"sheetH={SheetRoot.Bounds.Height:F0} | sheetY={SheetRoot.Bounds.Y:F0} | " +
            $"sheetBottom={SheetRoot.Bounds.Y + SheetRoot.Bounds.Height:F0}");
        _lastKbOffset = offset;
    }

    /// <summary>清除键盘偏移，恢复默认状态。</summary>
    private void ClearKeyboardOffset(string reason)
    {
        if (SheetRoot is null) return;
        SheetRoot.Margin = new Thickness(0);
        SheetRoot.MaxHeight = DefaultMaxHeight;
        _lastKbOffset = 0;
        DevLogger.Log("SheetView", $"ClearOffset | {reason} | restored default");
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
