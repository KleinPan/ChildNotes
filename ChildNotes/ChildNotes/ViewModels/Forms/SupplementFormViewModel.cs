using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

/// <summary>
/// 常用选项项模型
/// </summary>
public partial class CommonItemViewModel : ObservableObject
{
    public string Name { get; }

    [ObservableProperty] private bool _isSelected;

    public CommonItemViewModel(string name)
    {
        Name = name;
        _isSelected = false;
    }
}

public partial class SupplementFormViewModel : ObservableObject, IRecordFormViewModel
{
    [ObservableProperty] private string _dateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _suppType = "supplement";
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _dose = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);
    [ObservableProperty] private string _customItem = string.Empty;

    /// <summary>
    /// 常用药品选项（用药类型）- 对齐小程序 DEFAULT_MEDICINES
    /// </summary>
    public ObservableCollection<CommonItemViewModel> MedicineCommonItems { get; } = new(new[]
    {
        new CommonItemViewModel("泰诺林"),
        new CommonItemViewModel("布洛芬"),
        new CommonItemViewModel("美林"),
        new CommonItemViewModel("蒙脱石散"),
        new CommonItemViewModel("口服补液盐"),
        new CommonItemViewModel("西替利嗪"),
    });

    /// <summary>
    /// 常用补充剂选项（补充剂类型）- 对齐小程序 DEFAULT_SUPPLEMENTS
    /// </summary>
    public ObservableCollection<CommonItemViewModel> SupplementCommonItems { get; } = new(new[]
    {
        new CommonItemViewModel("维生素D"),
        new CommonItemViewModel("益生菌"),
        new CommonItemViewModel("DHA"),
        new CommonItemViewModel("钙剂"),
        new CommonItemViewModel("铁剂"),
        new CommonItemViewModel("锌剂"),
    });

    /// <summary>
    /// 获取当前类型的常用选项列表
    /// </summary>
    public ObservableCollection<CommonItemViewModel> CurrentCommonItems =>
        SuppType == "medicine" ? MedicineCommonItems : SupplementCommonItems;

    /// <summary>
    /// 添加自定义内容命令
    /// </summary>
    public ICommand AddCustomCommand { get; }

    public SupplementFormViewModel()
    {
        AddCustomCommand = new RelayCommand(AddCustomItem);
    }

    public void SwitchType(string type) => SuppType = type;

    /// <summary>
    /// 添加自定义内容
    /// </summary>
    public void AddCustomItem()
    {
        if (!string.IsNullOrWhiteSpace(CustomItem))
        {
            // 将自定义内容追加到名称字段
            if (!string.IsNullOrWhiteSpace(Name))
                Name += $"、{CustomItem}";
            else
                Name = CustomItem;

            CustomItem = string.Empty;
        }
    }

    /// <summary>
    /// 类型切换时：清空旧选中状态、清空名称、通知 UI 刷新常用项列表
    /// </summary>
    partial void OnSuppTypeChanged(string value)
    {
        // 清空旧类型的选中状态
        foreach (var item in MedicineCommonItems) item.IsSelected = false;
        foreach (var item in SupplementCommonItems) item.IsSelected = false;
        // 清空名称（对齐小程序 switchType 中 name: '' 逻辑）
        Name = string.Empty;
        // 清空自定义输入
        CustomItem = string.Empty;
        // 通知 UI 刷新常用项列表
        OnPropertyChanged(nameof(CurrentCommonItems));
    }

    /// <summary>
    /// 当选中项变化时刷新名称
    /// </summary>
    public void RefreshNameFromSelection()
    {
        var selected = CurrentCommonItems.Where(x => x.IsSelected).Select(x => x.Name).ToList();
        Name = string.Join("、", selected);
    }

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "请输入或选择名称";
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
        Note = Note,
        Time = $"{DateText} {TimeText}",
    };
}
