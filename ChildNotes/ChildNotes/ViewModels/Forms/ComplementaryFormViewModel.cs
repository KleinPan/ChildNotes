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
    [ObservableProperty] private string _dateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _foodName = string.Empty;
    [ObservableProperty] private string _selectedTexture = "puree";
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _selectedReaction = "none";
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);
    [ObservableProperty] private string _customFood = string.Empty;

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
        Reaction = SelectedReaction,
        Abnormal = SelectedReaction is "allergy" or "vomit" or "diarrhea",
        Note = Note,
        Time = $"{DateText} {TimeText}",
    };
}
