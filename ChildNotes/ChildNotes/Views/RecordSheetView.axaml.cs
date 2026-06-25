using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using ChildNotes.Models;
using ChildNotes.Services;
using ChildNotes.ViewModels;
using System.Globalization;

namespace ChildNotes.Views;

public partial class RecordSheetView : UserControl
{
    public RecordSheetView()
    {
        InitializeComponent();
    }

    public static readonly IValueConverter IsFeedConverter = new EqualsConverter(RecordType.Feed);
    public static readonly IValueConverter IsDiaperConverter = new EqualsConverter(RecordType.Diaper);
    public static readonly IValueConverter IsSleepConverter = new EqualsConverter(RecordType.Sleep);
    public static readonly IValueConverter IsTempConverter = new EqualsConverter(RecordType.Temperature);
    public static readonly IValueConverter IsGrowthConverter = new EqualsConverter(RecordType.Growth);
    public static readonly IValueConverter IsSupplementConverter = new EqualsConverter(RecordType.Supplement);
    public static readonly IValueConverter IsPumpConverter = new EqualsConverter(RecordType.Pump);
    public static readonly IValueConverter IsComplementaryConverter = new EqualsConverter(RecordType.Complementary);
    public static readonly IValueConverter IsAbnormalConverter = new EqualsConverter(RecordType.Abnormal);
    public static readonly IValueConverter IsVaccineConverter = new EqualsConverter(RecordType.Vaccine);
    public static readonly IValueConverter IsNotVaccineConverter = new NotEqualsConverter(RecordType.Vaccine);
    public static readonly IValueConverter IsActivityConverter = new EqualsConverter(RecordType.Activity);

    public static readonly IValueConverter IsBottleConverter = new EqualsConverter("bottle");
    public static readonly IValueConverter IsBreastConverter = new EqualsConverter("breast");
    public static readonly IValueConverter IsExpressedConverter = new EqualsConverter("expressed");
    public static readonly IValueConverter IsNotBreastConverter = new NotEqualsConverter("breast");

    public static readonly IValueConverter IsWetConverter = new EqualsConverter("wet");
    public static readonly IValueConverter IsDirtyConverter = new EqualsConverter("dirty");
    public static readonly IValueConverter IsBothConverter = new EqualsConverter("both");
    public static readonly IValueConverter IsDryConverter = new EqualsConverter("dry");

    public static readonly IValueConverter IsMedicineConverter = new EqualsConverter("medicine");
    public static readonly IValueConverter IsNutritionConverter = new EqualsConverter("nutrition");

    public static readonly IValueConverter IsPureeConverter = new EqualsConverter("puree");
    public static readonly IValueConverter IsMashedConverter = new EqualsConverter("mashed");
    public static readonly IValueConverter IsLumpyConverter = new EqualsConverter("lumpy");
    public static readonly IValueConverter IsNoneReactionConverter = new EqualsConverter("none");
    public static readonly IValueConverter IsAllergyConverter = new EqualsConverter("allergy");
    public static readonly IValueConverter IsVomitReactionConverter = new EqualsConverter("vomit");
    public static readonly IValueConverter IsDiarrheaReactionConverter = new EqualsConverter("diarrhea");

    public static readonly IValueConverter IsPlayConverter = new EqualsConverter("play");
    public static readonly IValueConverter IsOutdoorConverter = new EqualsConverter("outdoor");
    public static readonly IValueConverter IsExerciseConverter = new EqualsConverter("exercise");

    // 疫苗自定义类型分段
    public static readonly IValueConverter IsCustomFreeConverter = new EqualsConverter("free");
    public static readonly IValueConverter IsCustomPaidConverter = new EqualsConverter("paid");

    // 疫苗时间轴状态
    public static readonly IValueConverter IsStatusDoneConverter = new EqualsConverter(VaccineDoseStatus.Done);
    public static readonly IValueConverter IsStatusSkippedConverter = new EqualsConverter(VaccineDoseStatus.Skipped);
    public static readonly IValueConverter IsStatusReplacedConverter = new EqualsConverter(VaccineDoseStatus.Replaced);
    public static readonly IValueConverter IsStatusOverdueConverter = new EqualsConverter(VaccineDoseStatus.Overdue);
    public static readonly IValueConverter IsStatusDueConverter = new EqualsConverter(VaccineDoseStatus.Due);
    public static readonly IValueConverter IsStatusSoonConverter = new EqualsConverter(VaccineDoseStatus.Soon);
    public static readonly IValueConverter IsStatusPendingConverter = new EqualsConverter(VaccineDoseStatus.Pending);

    // 通用：非 null 转换器（用于 SelectedPlan 可见性）
    public static readonly IValueConverter IsNotNullConverter = new FuncValueConverter<object?, bool>(o => o is not null);

    // 疫苗折叠按钮文案：true→收起，false→+ 添加
    public static readonly IValueConverter VaccineToggleTextConverter = new FuncValueConverter<bool, string>(
        isExpanded => isExpanded ? "收起" : "+ 添加");

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
}
