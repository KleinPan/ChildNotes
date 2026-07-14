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

/// <summary>
/// 常用选项项模型。IsCustom 区分系统默认项与用户自定义项（影响样式与删除能力）。
/// </summary>
public partial class CommonItemViewModel : ObservableObject
{
    public string Name { get; }
    /// <summary>true 表示用户自定义项（白底绿描边样式，可长按删除）；false 表示系统默认项（灰底）。</summary>
    public bool IsCustom { get; }

    [ObservableProperty] private bool _isSelected;

    public CommonItemViewModel(string name, bool isCustom = false)
    {
        Name = name;
        IsCustom = isCustom;
        _isSelected = false;
    }
}

public partial class SupplementFormViewModel : ObservableObject, IRecordFormViewModel
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    // 系统默认剂量单位（对齐小程序 DEFAULT_DOSE_UNITS = ['ml', '粒', '包']）
    private static readonly string[] DefaultDoseUnits = { "ml", "粒", "包" };

    [ObservableProperty] private string _dateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _suppType = "supplement";
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _dose = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);
    [ObservableProperty] private string _customItem = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    // 剂量单位相关
    [ObservableProperty] private string _doseUnit = "ml";
    [ObservableProperty] private string _customUnit = string.Empty;
    [ObservableProperty] private bool _showCustomUnitInput;

    /// <summary>系统默认剂量单位（不可删除）</summary>
    public ObservableCollection<CommonItemViewModel> DefaultDoseUnitItems { get; } = new(DefaultDoseUnits.Select(u => new CommonItemViewModel(u, isCustom: false)));

    /// <summary>用户自定义剂量单位（从DB加载，IsCustom=true，可删除）</summary>
    public ObservableCollection<CommonItemViewModel> CustomDoseUnitItems { get; } = new();

    /// <summary>当前可选单位列表（默认 + 自定义），供 UI 绑定</summary>
    public ObservableCollection<CommonItemViewModel> AllDoseUnitItems { get; } = new();

    /// <summary>系统默认补充剂项（5项，对齐小程序 DEFAULT_SUPPLEMENTS）</summary>
    public ObservableCollection<CommonItemViewModel> SupplementCommonItems { get; } = new(new[]
    {
        new CommonItemViewModel("维生素D"),
        new CommonItemViewModel("益生菌"),
        new CommonItemViewModel("DHA"),
        new CommonItemViewModel("钙剂"),
        new CommonItemViewModel("铁剂"),
    });

    /// <summary>系统默认用药项（5项，对齐小程序 DEFAULT_MEDICINES）</summary>
    public ObservableCollection<CommonItemViewModel> MedicineCommonItems { get; } = new(new[]
    {
        new CommonItemViewModel("泰诺林"),
        new CommonItemViewModel("布洛芬"),
        new CommonItemViewModel("美林"),
        new CommonItemViewModel("蒙脱石散"),
        new CommonItemViewModel("口服补液盐"),
    });

    /// <summary>用户自定义补充剂项（从DB加载，IsCustom=true）</summary>
    public ObservableCollection<CommonItemViewModel> CustomSupplementItems { get; } = new();

    /// <summary>用户自定义用药项（从DB加载，IsCustom=true）</summary>
    public ObservableCollection<CommonItemViewModel> CustomMedicineItems { get; } = new();

    /// <summary>
    /// 当前类型下的全部可选项（默认项 + 自定义项），供 UI 绑定。
    /// 切换类型时通过 OnSuppTypeChanged 触发刷新。
    /// </summary>
    public ObservableCollection<CommonItemViewModel> CurrentAllItems { get; } = new();

    /// <summary>添加自定义内容命令</summary>
    public ICommand AddCustomCommand { get; }

    /// <summary>删除自定义项命令（参数为 CommonItemViewModel）</summary>
    public ICommand DeleteCustomCommand { get; }

    /// <summary>添加自定义单位命令</summary>
    public ICommand AddCustomUnitCommand { get; }

    /// <summary>删除自定义单位命令（参数为 CommonItemViewModel）</summary>
    public ICommand DeleteCustomUnitCommand { get; }

    /// <summary>切换自定义单位输入区显示/隐藏</summary>
    public ICommand ToggleCustomUnitInputCommand { get; }

    public SupplementFormViewModel()
    {
        AddCustomCommand = new RelayCommand(AddCustomItem);
        DeleteCustomCommand = new RelayCommand<CommonItemViewModel>(DeleteCustomItem);
        AddCustomUnitCommand = new RelayCommand(AddCustomUnit);
        DeleteCustomUnitCommand = new RelayCommand<CommonItemViewModel>(DeleteCustomUnit);
        ToggleCustomUnitInputCommand = new RelayCommand(() => ShowCustomUnitInput = !ShowCustomUnitInput);
        // 订阅所有默认项的属性变化
        SubscribeItems(SupplementCommonItems);
        SubscribeItems(MedicineCommonItems);
        // 订阅默认单位选中变化
        SubscribeItems(DefaultDoseUnitItems);
        // 初始加载自定义项并合并到 CurrentAllItems
        ReloadCustomItems();
        // 加载自定义单位
        ReloadCustomUnits();
        // 默认选中第一个单位
        if (DefaultDoseUnitItems.Count > 0) DefaultDoseUnitItems[0].IsSelected = true;
    }

    private void SubscribeItems(ObservableCollection<CommonItemViewModel> items)
    {
        foreach (var item in items) item.PropertyChanged += OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommonItemViewModel.IsSelected)) return;
        // 判断 sender 属于哪个集合：单位项变化 → 更新 DoseUnit；名称项变化 → 重建 Name
        if (sender is CommonItemViewModel item)
        {
            if (DefaultDoseUnitItems.Contains(item) || CustomDoseUnitItems.Contains(item))
                RebuildDoseUnit();
            else
                RebuildName();
        }
    }

    public void SwitchType(string type) => SuppType = type;

    /// <summary>
    /// 从DB重新加载当前类型的自定义项，并重建 CurrentAllItems（默认项在前，自定义项在后）。
    /// </summary>
    private void ReloadCustomItems()
    {
        var userId = ServiceProvider.Instance.AppState.UserId;
        var repo = ServiceProvider.Instance.SupplementItemRepository;

        var customSupps = string.IsNullOrEmpty(userId)
            ? new List<CommonItemViewModel>()
            : repo.GetByUser(userId, "supplement")
                  .Select(x => new CommonItemViewModel(x.Name, isCustom: true)).ToList();
        var customMeds = string.IsNullOrEmpty(userId)
            ? new List<CommonItemViewModel>()
            : repo.GetByUser(userId, "medicine")
                  .Select(x => new CommonItemViewModel(x.Name, isCustom: true)).ToList();

        // 订阅新加载项的属性变化
        foreach (var item in customSupps) item.PropertyChanged += OnItemPropertyChanged;
        foreach (var item in customMeds) item.PropertyChanged += OnItemPropertyChanged;

        CustomSupplementItems.Clear();
        foreach (var i in customSupps) CustomSupplementItems.Add(i);
        CustomMedicineItems.Clear();
        foreach (var i in customMeds) CustomMedicineItems.Add(i);

        RebuildCurrentAllItems();
    }

    /// <summary>合并当前类型的默认项 + 自定义项到 CurrentAllItems。</summary>
    private void RebuildCurrentAllItems()
    {
        // 先解绑旧项的事件，避免内存泄漏
        foreach (var item in CurrentAllItems) item.PropertyChanged -= OnItemPropertyChanged;
        CurrentAllItems.Clear();

        var defaults = SuppType == "medicine" ? MedicineCommonItems : SupplementCommonItems;
        var customs = SuppType == "medicine" ? CustomMedicineItems : CustomSupplementItems;
        foreach (var item in defaults) CurrentAllItems.Add(item);
        foreach (var item in customs) CurrentAllItems.Add(item);
        // CurrentAllItems 中的项本身就是 defaults/customs 中的同一个引用，事件已订阅，无需重复订阅
    }

    /// <summary>
    /// 添加自定义内容：校验去重 → 写入DB → 加入集合 → 自动选中。
    /// </summary>
    public void AddCustomItem()
    {
        ErrorMessage = string.Empty;
        var value = CustomItem?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            ErrorMessage = _locale.GetString("Form_ErrSupplementName", "请输入名称");
            return;
        }

        // 去重：默认项和当前类型的自定义项都不能重复
        var defaults = SuppType == "medicine" ? MedicineCommonItems : SupplementCommonItems;
        var customs = SuppType == "medicine" ? CustomMedicineItems : CustomSupplementItems;
        if (defaults.Any(x => x.Name == value) || customs.Any(x => x.Name == value))
        {
            ErrorMessage = _locale.GetString("Form_ErrSupplementDuplicate", "该名称已存在");
            return;
        }

        // 写入DB（用户未登录时 userId 为空，仅内存保存）
        var userId = ServiceProvider.Instance.AppState.UserId;
        if (!string.IsNullOrEmpty(userId))
        {
            ServiceProvider.Instance.SupplementItemRepository.Insert(userId, SuppType, value);
        }

        // 加入集合并选中
        var newItem = new CommonItemViewModel(value, isCustom: true);
        newItem.PropertyChanged += OnItemPropertyChanged;
        customs.Add(newItem);
        CurrentAllItems.Add(newItem);
        newItem.IsSelected = true;  // 自动选中，触发 RebuildName

        CustomItem = string.Empty;
    }

    /// <summary>
    /// 删除自定义项（长按触发）：从DB和集合中移除，并同步 Name。
    /// </summary>
    private void DeleteCustomItem(CommonItemViewModel? item)
    {
        if (item is null || !item.IsCustom) return;
        ErrorMessage = string.Empty;

        var userId = ServiceProvider.Instance.AppState.UserId;
        if (!string.IsNullOrEmpty(userId))
        {
            ServiceProvider.Instance.SupplementItemRepository.Delete(userId, SuppType, item.Name);
        }

        var customs = SuppType == "medicine" ? CustomMedicineItems : CustomSupplementItems;
        customs.Remove(item);
        CurrentAllItems.Remove(item);
        item.PropertyChanged -= OnItemPropertyChanged;
        if (item.IsSelected) RebuildName();
    }

    /// <summary>合并"当前列表选中项"为 Name（自定义项通过 IsSelected 选中即可，无需单独累积）。</summary>
    private void RebuildName()
    {
        var selected = CurrentAllItems.Where(x => x.IsSelected).Select(x => x.Name).Distinct().ToList();
        Name = string.Join("、", selected);
    }

    #region 剂量单位管理

    /// <summary>
    /// 从DB重新加载用户自定义剂量单位（type="dose_unit"），并重建 AllDoseUnitItems。
    /// </summary>
    private void ReloadCustomUnits()
    {
        var userId = ServiceProvider.Instance.AppState.UserId;
        var customUnits = string.IsNullOrEmpty(userId)
            ? new List<CommonItemViewModel>()
            : ServiceProvider.Instance.SupplementItemRepository
                  .GetByUser(userId, "dose_unit")
                  .Select(x => new CommonItemViewModel(x.Name, isCustom: true)).ToList();

        // 解绑旧项事件
        foreach (var item in CustomDoseUnitItems) item.PropertyChanged -= OnItemPropertyChanged;
        CustomDoseUnitItems.Clear();
        foreach (var u in customUnits)
        {
            u.PropertyChanged += OnItemPropertyChanged;
            CustomDoseUnitItems.Add(u);
        }
        RebuildAllDoseUnitItems();
    }

    /// <summary>合并默认单位 + 自定义单位到 AllDoseUnitItems。</summary>
    private void RebuildAllDoseUnitItems()
    {
        AllDoseUnitItems.Clear();
        foreach (var item in DefaultDoseUnitItems) AllDoseUnitItems.Add(item);
        foreach (var item in CustomDoseUnitItems) AllDoseUnitItems.Add(item);
    }

    /// <summary>单位选中变化时更新 DoseUnit 字段（单选）。</summary>
    private void RebuildDoseUnit()
    {
        var selected = AllDoseUnitItems.FirstOrDefault(x => x.IsSelected);
        if (selected is not null) DoseUnit = selected.Name;
    }

    /// <summary>
    /// 编辑回填时按单位名称设置选中（用于外部设置 DoseUnit 后同步 UI）。
    /// </summary>
    public void SelectDoseUnitByName(string? unit)
    {
        // 清空所有选中
        foreach (var item in AllDoseUnitItems) item.IsSelected = false;
        if (string.IsNullOrEmpty(unit)) return;
        var match = AllDoseUnitItems.FirstOrDefault(x => x.Name == unit);
        if (match is not null) match.IsSelected = true;
        DoseUnit = unit;
    }

    /// <summary>添加自定义剂量单位：校验去重 → 写入DB → 加入集合 → 自动选中。</summary>
    public void AddCustomUnit()
    {
        ErrorMessage = string.Empty;
        var value = CustomUnit?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            ErrorMessage = _locale.GetString("Form_ErrUnitName", "请输入单位");
            return;
        }
        // 去重：默认单位和自定义单位都不能重复
        if (DefaultDoseUnitItems.Any(x => x.Name == value) || CustomDoseUnitItems.Any(x => x.Name == value))
        {
            ErrorMessage = _locale.GetString("Form_ErrUnitDuplicate", "该单位已存在");
            return;
        }

        var userId = ServiceProvider.Instance.AppState.UserId;
        if (!string.IsNullOrEmpty(userId))
        {
            ServiceProvider.Instance.SupplementItemRepository.Insert(userId, "dose_unit", value);
        }

        var newItem = new CommonItemViewModel(value, isCustom: true);
        newItem.PropertyChanged += OnItemPropertyChanged;
        CustomDoseUnitItems.Add(newItem);
        AllDoseUnitItems.Add(newItem);
        // 自动选中
        foreach (var item in AllDoseUnitItems) item.IsSelected = false;
        newItem.IsSelected = true;

        CustomUnit = string.Empty;
        ShowCustomUnitInput = false;
    }

    /// <summary>删除自定义剂量单位（长按触发）。</summary>
    private void DeleteCustomUnit(CommonItemViewModel? item)
    {
        if (item is null || !item.IsCustom) return;
        ErrorMessage = string.Empty;

        var userId = ServiceProvider.Instance.AppState.UserId;
        if (!string.IsNullOrEmpty(userId))
        {
            ServiceProvider.Instance.SupplementItemRepository.Delete(userId, "dose_unit", item.Name);
        }

        var wasSelected = item.IsSelected;
        CustomDoseUnitItems.Remove(item);
        AllDoseUnitItems.Remove(item);
        item.PropertyChanged -= OnItemPropertyChanged;
        // 删除的是当前选中项 → 回退到第一个默认单位
        if (wasSelected && DefaultDoseUnitItems.Count > 0)
        {
            foreach (var it in AllDoseUnitItems) it.IsSelected = false;
            DefaultDoseUnitItems[0].IsSelected = true;
        }
    }

    #endregion

    /// <summary>
    /// 类型切换时：清空选中状态、重新加载对应类型的自定义项、通知 UI 刷新。
    /// </summary>
    partial void OnSuppTypeChanged(string value)
    {
        // 清空所有选中状态（默认项 + 自定义项）
        foreach (var item in SupplementCommonItems) item.IsSelected = false;
        foreach (var item in MedicineCommonItems) item.IsSelected = false;
        foreach (var item in CustomSupplementItems) item.IsSelected = false;
        foreach (var item in CustomMedicineItems) item.IsSelected = false;
        Name = string.Empty;
        CustomItem = string.Empty;
        ErrorMessage = string.Empty;
        // 重建 CurrentAllItems（切换默认项 + 对应自定义项）
        RebuildCurrentAllItems();
    }

    /// <summary>当选中项变化时刷新名称（保留以兼容测试）。</summary>
    public void RefreshNameFromSelection() => RebuildName();

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = _locale.GetString("Form_ErrSupplementNameOrSelect", "请输入或选择名称");
            return false;
        }
        error = string.Empty;
        return true;
    }

    public SupplementRecordDto BuildDto() => new()
    {
        Type = SuppType,
        Name = Name,
        Dose = Dose,
        DoseUnit = DoseUnit,
        Note = Note,
        Time = $"{DateText} {TimeText}",
    };
}
