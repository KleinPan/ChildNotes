using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

public partial class ComplementaryFormViewModel : ObservableObject, IRecordFormViewModel
{
    // 辅食食量单位（对齐小程序 AMOUNT_UNITS = ['克', '个', '勺', '碗']，固定不可自定义）
    private static readonly string[] DefaultAmountUnits = { "克", "个", "勺", "碗" };

    [ObservableProperty] private string _dateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _foodName = string.Empty;
    [ObservableProperty] private string _selectedTexture = "puree";
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _selectedReaction = "none";
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);
    [ObservableProperty] private string _customFood = string.Empty;

    /// <summary>当前选中的食量单位（默认"克"）</summary>
    [ObservableProperty] private string _amountUnit = "克";

    /// <summary>可选食量单位列表（固定 4 项，对齐小程序）</summary>
    public ObservableCollection<CommonItemViewModel> AmountUnitItems { get; } = new(DefaultAmountUnits.Select(u => new CommonItemViewModel(u)));

    /// <summary>
    /// 常用辅食选项（精选最常用，适合两行显示）
    /// </summary>
    public ObservableCollection<CommonItemViewModel> StapleFoodItems { get; } = new(new[]
    {
        new CommonItemViewModel("米粉"),
        new CommonItemViewModel("面条"),
        new CommonItemViewModel("小米粥"),
    });

    /// <summary>
    /// 常用蔬菜选项
    /// </summary>
    public ObservableCollection<CommonItemViewModel> VegetableItems { get; } = new(new[]
    {
        new CommonItemViewModel("南瓜泥"),
        new CommonItemViewModel("土豆泥"),
        new CommonItemViewModel("胡萝卜泥"),
    });

    /// <summary>
    /// 常用水果选项
    /// </summary>
    public ObservableCollection<CommonItemViewModel> FruitItems { get; } = new(new[]
    {
        new CommonItemViewModel("苹果泥"),
        new CommonItemViewModel("香蕉泥"),
        new CommonItemViewModel("牛油果"),
    });

    /// <summary>
    /// 常用肉蛋选项
    /// </summary>
    public ObservableCollection<CommonItemViewModel> MeatEggItems { get; } = new(new[]
    {
        new CommonItemViewModel("蛋黄"),
        new CommonItemViewModel("鸡肉泥"),
        new CommonItemViewModel("鱼肉泥"),
    });

    /// <summary>
    /// 所有常用辅食选项（合并展示）
    /// </summary>
    public ObservableCollection<CommonItemViewModel> AllCommonFoodItems { get; }

    /// <summary>
    /// 添加自定义食物命令
    /// </summary>
    public ICommand AddCustomCommand { get; }

    public ComplementaryFormViewModel()
    {
        // 合并所有类别
        AllCommonFoodItems = new ObservableCollection<CommonItemViewModel>(
            StapleFoodItems.Concat(VegetableItems).Concat(FruitItems).Concat(MeatEggItems));

        AddCustomCommand = new RelayCommand(AddCustomFood);

        // 订阅单位项选中变化，默认选中第一个
        foreach (var item in AmountUnitItems) item.PropertyChanged += OnAmountUnitChanged;
        if (AmountUnitItems.Count > 0) AmountUnitItems[0].IsSelected = true;
    }

    /// <summary>单位 Chip 选中变化时同步到 AmountUnit 字段（单选清空由 code-behind 处理）。</summary>
    private void OnAmountUnitChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommonItemViewModel.IsSelected)) return;
        if (sender is CommonItemViewModel item && item.IsSelected)
        {
            AmountUnit = item.Name;
        }
    }

    /// <summary>编辑回填时按单位名称设置选中（用于外部设置 AmountUnit 后同步 UI）。</summary>
    public void SelectAmountUnitByName(string? unit)
    {
        foreach (var item in AmountUnitItems) item.IsSelected = false;
        if (string.IsNullOrEmpty(unit)) return;
        var match = AmountUnitItems.FirstOrDefault(x => x.Name == unit);
        if (match is not null) match.IsSelected = true;
        AmountUnit = unit;
    }

    public void SelectTexture(string t) => SelectedTexture = t;
    public void SelectReaction(string r) => SelectedReaction = r;

    /// <summary>
    /// 添加自定义食物
    /// </summary>
    public void AddCustomFood()
    {
        if (!string.IsNullOrWhiteSpace(CustomFood))
        {
            if (!string.IsNullOrWhiteSpace(FoodName))
                FoodName += $"、{CustomFood}";
            else
                FoodName = CustomFood;

            CustomFood = string.Empty;
        }
    }

    /// <summary>
    /// 同步选中项到食物名称字段
    /// </summary>
    public void RefreshFoodNameFromSelection()
    {
        var selected = AllCommonFoodItems.Where(x => x.IsSelected).Select(x => x.Name).ToList();
        FoodName = string.Join("、", selected);
    }

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(FoodName))
        {
            error = "请输入或选择食物名称";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public ComplementaryRecordDto BuildDto() => new()
    {
        FoodTypes = AllCommonFoodItems.Where(x => x.IsSelected).Select(x => x.Name).ToList(),
        FoodName = FoodName,
        Texture = SelectedTexture,
        Amount = AmountText,
        AmountUnit = AmountUnit,
        Reaction = SelectedReaction,
        Abnormal = SelectedReaction is "allergy" or "vomit" or "diarrhea",
        Note = Note,
        Time = $"{DateText} {TimeText}",
    };
}
