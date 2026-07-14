using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

public partial class ComplementaryFormViewModel : ObservableObject, IRecordFormViewModel
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    // 辅食食量单位（对齐小程序 AMOUNT_UNITS = ['克', '个', '勺', '碗']，固定不可自定义）
    private static readonly string[] DefaultAmountUnits = { "克", "个", "勺", "碗" };

    // SupplementItemRepository 中存储自定义辅食的 type 标识
    private const string CustomFoodType = "custom_food";

    [ObservableProperty] private string _dateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _foodName = string.Empty;
    [ObservableProperty] private string _selectedTexture = "puree";
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _selectedReaction = "none";
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);
    [ObservableProperty] private string _customFood = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

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

    /// <summary>系统默认辅食项（不可删除）</summary>
    public ObservableCollection<CommonItemViewModel> DefaultFoodItems { get; }

    /// <summary>用户自定义辅食项（从 DB 加载，IsCustom=true，可删除）</summary>
    public ObservableCollection<CommonItemViewModel> CustomFoodItems { get; } = new();

    /// <summary>
    /// 所有常用辅食选项（默认项 + 自定义项，合并展示）
    /// </summary>
    public ObservableCollection<CommonItemViewModel> AllCommonFoodItems { get; } = new();

    /// <summary>添加自定义食物命令</summary>
    public ICommand AddCustomCommand { get; }

    /// <summary>删除自定义食物命令（参数为 CommonItemViewModel）</summary>
    public ICommand DeleteCustomCommand { get; }

    public ComplementaryFormViewModel()
    {
        // 合并所有默认类别
        DefaultFoodItems = new ObservableCollection<CommonItemViewModel>(
            StapleFoodItems.Concat(VegetableItems).Concat(FruitItems).Concat(MeatEggItems));

        AddCustomCommand = new RelayCommand(AddCustomFood);
        DeleteCustomCommand = new RelayCommand<CommonItemViewModel>(DeleteCustomFood);

        // 订阅默认项选中变化
        SubscribeItems(DefaultFoodItems);
        // 订阅单位项选中变化，默认选中第一个
        foreach (var item in AmountUnitItems) item.PropertyChanged += OnAmountUnitChanged;
        if (AmountUnitItems.Count > 0) AmountUnitItems[0].IsSelected = true;

        // 加载自定义辅食并合并到 AllCommonFoodItems
        ReloadCustomFoods();
    }

    private void SubscribeItems(ObservableCollection<CommonItemViewModel> items)
    {
        foreach (var item in items) item.PropertyChanged += OnFoodItemChanged;
    }

    /// <summary>食物项选中变化时同步重建 FoodName。</summary>
    private void OnFoodItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommonItemViewModel.IsSelected)) return;
        RebuildFoodName();
    }

    /// <summary>合并选中项到 FoodName（以"、"分隔）。</summary>
    private void RebuildFoodName()
    {
        var selected = AllCommonFoodItems.Where(x => x.IsSelected).Select(x => x.Name).Distinct().ToList();
        FoodName = string.Join("、", selected);
    }

    /// <summary>从 DB 重新加载自定义辅食，并重建 AllCommonFoodItems。</summary>
    private void ReloadCustomFoods()
    {
        var userId = ServiceProvider.Instance.AppState.UserId;
        var customFoods = string.IsNullOrEmpty(userId)
            ? new List<CommonItemViewModel>()
            : ServiceProvider.Instance.SupplementItemRepository
                  .GetByUser(userId, CustomFoodType)
                  .Select(x => new CommonItemViewModel(x.Name, isCustom: true)).ToList();

        foreach (var item in customFoods) item.PropertyChanged += OnFoodItemChanged;

        CustomFoodItems.Clear();
        foreach (var i in customFoods) CustomFoodItems.Add(i);

        RebuildAllFoodItems();
    }

    /// <summary>合并默认项 + 自定义项到 AllCommonFoodItems。</summary>
    private void RebuildAllFoodItems()
    {
        AllCommonFoodItems.Clear();
        foreach (var item in DefaultFoodItems) AllCommonFoodItems.Add(item);
        foreach (var item in CustomFoodItems) AllCommonFoodItems.Add(item);
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

    /// <summary>编辑回填时按 FoodName 反选 Chip（匹配到的项置为选中）。</summary>
    public void SelectFoodsByName(string? foodName)
    {
        foreach (var item in AllCommonFoodItems) item.IsSelected = false;
        if (string.IsNullOrWhiteSpace(foodName)) return;
        var names = foodName.Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var name in names)
        {
            var match = AllCommonFoodItems.FirstOrDefault(x => x.Name == name);
            if (match is not null) match.IsSelected = true;
        }
    }

    public void SelectTexture(string t) => SelectedTexture = t;
    public void SelectReaction(string r) => SelectedReaction = r;

    /// <summary>
    /// 添加自定义食物：校验去重 → 写入 DB → 加入集合并选中 → 清空输入框。
    /// </summary>
    public void AddCustomFood()
    {
        ErrorMessage = string.Empty;
        var value = CustomFood?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            ErrorMessage = _locale.GetString("Form_ErrFoodName", "请输入食物名称");
            return;
        }

        // 去重：默认项和自定义项都不能重复
        if (DefaultFoodItems.Any(x => x.Name == value) || CustomFoodItems.Any(x => x.Name == value))
        {
            ErrorMessage = _locale.GetString("Form_ErrFoodDuplicate", "该食物已存在");
            return;
        }

        // 写入 DB（用户未登录时仅内存保存）
        var userId = ServiceProvider.Instance.AppState.UserId;
        if (!string.IsNullOrEmpty(userId))
        {
            ServiceProvider.Instance.SupplementItemRepository.Insert(userId, CustomFoodType, value);
        }

        // 加入集合并选中
        var newItem = new CommonItemViewModel(value, isCustom: true);
        newItem.PropertyChanged += OnFoodItemChanged;
        CustomFoodItems.Add(newItem);
        AllCommonFoodItems.Add(newItem);
        newItem.IsSelected = true;  // 自动选中，触发 RebuildFoodName

        CustomFood = string.Empty;
    }

    /// <summary>删除自定义食物（右键触发）：从 DB 和集合中移除，并同步 FoodName。</summary>
    private void DeleteCustomFood(CommonItemViewModel? item)
    {
        if (item is null || !item.IsCustom) return;
        ErrorMessage = string.Empty;

        var userId = ServiceProvider.Instance.AppState.UserId;
        if (!string.IsNullOrEmpty(userId))
        {
            ServiceProvider.Instance.SupplementItemRepository.Delete(userId, CustomFoodType, item.Name);
        }

        CustomFoodItems.Remove(item);
        AllCommonFoodItems.Remove(item);
        item.PropertyChanged -= OnFoodItemChanged;
        if (item.IsSelected) RebuildFoodName();
    }

    /// <summary>
    /// 同步选中项到食物名称字段
    /// </summary>
    public void RefreshFoodNameFromSelection()
    {
        RebuildFoodName();
    }

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(FoodName))
        {
            error = _locale.GetString("Form_ErrFoodNameOrSelect", "请输入或选择食物名称");
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
