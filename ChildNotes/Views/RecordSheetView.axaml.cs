using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using ChildNotes.Models;
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
}
