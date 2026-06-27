using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using ChildNotes.Models;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class QuickRecordCardView : UserControl
{
    public QuickRecordCardView()
    {
        InitializeComponent();
    }

    // ===== MaternalFood 类型转换器（RecordSheetView 中未定义） =====
    public static readonly IValueConverter IsMaternalFoodConverter = new EqualsConverter(RecordType.MaternalFood);

    public static readonly IValueConverter IsBreakfastConverter = new EqualsConverter("breakfast");
    public static readonly IValueConverter IsLunchConverter = new EqualsConverter("lunch");
    public static readonly IValueConverter IsDinnerConverter = new EqualsConverter("dinner");
    public static readonly IValueConverter IsSnackConverter = new EqualsConverter("snack");

    public static readonly IValueConverter IsNoSuspicionConverter = new EqualsConverter("none");
    public static readonly IValueConverter IsMildSuspicionConverter = new EqualsConverter("mild");
    public static readonly IValueConverter IsHighSuspicionConverter = new EqualsConverter("high");

    // ===== 遮罩点击关闭卡片 =====
    private void OnMaskPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is QuickMenuViewModel vm)
        {
            vm.CloseCardCommand.Execute(null);
        }
    }

    // ===== 喂奶类型切换 =====
    private void OnFeedBottle(object sender, PointerPressedEventArgs e) => SwitchFeed("bottle");
    private void OnFeedBreast(object sender, PointerPressedEventArgs e) => SwitchFeed("breast");
    private void OnFeedExpressed(object sender, PointerPressedEventArgs e) => SwitchFeed("expressed");
    private void SwitchFeed(string t) { if (DataContext is QuickMenuViewModel vm) vm.RecordSheet.FeedForm.SwitchType(t); }

    // ===== 尿布类型切换 =====
    private void OnDiaperWet(object sender, PointerPressedEventArgs e) => SwitchDiaper("wet");
    private void OnDiaperDirty(object sender, PointerPressedEventArgs e) => SwitchDiaper("dirty");
    private void OnDiaperBoth(object sender, PointerPressedEventArgs e) => SwitchDiaper("both");
    private void OnDiaperDry(object sender, PointerPressedEventArgs e) => SwitchDiaper("dry");
    private void SwitchDiaper(string t) { if (DataContext is QuickMenuViewModel vm) vm.RecordSheet.DiaperForm.SelectType(t); }

    // ===== 补给类型切换 =====
    private void OnSuppMedicine(object sender, PointerPressedEventArgs e) => SwitchSupp("medicine");
    private void OnSuppNutrition(object sender, PointerPressedEventArgs e) => SwitchSupp("nutrition");
    private void SwitchSupp(string t) { if (DataContext is QuickMenuViewModel vm) vm.RecordSheet.SupplementForm.SwitchType(t); }

    // ===== 辅食质地/反应切换 =====
    private void OnTexturePuree(object sender, PointerPressedEventArgs e) => SwitchTexture("puree");
    private void OnTextureMashed(object sender, PointerPressedEventArgs e) => SwitchTexture("mashed");
    private void OnTextureLumpy(object sender, PointerPressedEventArgs e) => SwitchTexture("lumpy");
    private void SwitchTexture(string t) { if (DataContext is QuickMenuViewModel vm) vm.RecordSheet.ComplementaryForm.SelectTexture(t); }

    private void OnReactionNone(object sender, PointerPressedEventArgs e) => SwitchReaction("none");
    private void OnReactionAllergy(object sender, PointerPressedEventArgs e) => SwitchReaction("allergy");
    private void OnReactionVomit(object sender, PointerPressedEventArgs e) => SwitchReaction("vomit");
    private void OnReactionDiarrhea(object sender, PointerPressedEventArgs e) => SwitchReaction("diarrhea");
    private void SwitchReaction(string r) { if (DataContext is QuickMenuViewModel vm) vm.RecordSheet.ComplementaryForm.SelectReaction(r); }

    // ===== 妈妈饮食餐次/疑似过敏切换 =====
    private void OnMealBreakfast(object sender, PointerPressedEventArgs e) => SwitchMeal("breakfast");
    private void OnMealLunch(object sender, PointerPressedEventArgs e) => SwitchMeal("lunch");
    private void OnMealDinner(object sender, PointerPressedEventArgs e) => SwitchMeal("dinner");
    private void OnMealSnack(object sender, PointerPressedEventArgs e) => SwitchMeal("snack");
    private void SwitchMeal(string t) { if (DataContext is QuickMenuViewModel vm) vm.RecordSheet.MaternalFoodForm.SelectMealType(t); }

    private void OnSuspicionNone(object sender, PointerPressedEventArgs e) => SwitchSuspicion("none");
    private void OnSuspicionMild(object sender, PointerPressedEventArgs e) => SwitchSuspicion("mild");
    private void OnSuspicionHigh(object sender, PointerPressedEventArgs e) => SwitchSuspicion("high");
    private void SwitchSuspicion(string l) { if (DataContext is QuickMenuViewModel vm) vm.RecordSheet.MaternalFoodForm.SelectSuspicionLevel(l); }
}
