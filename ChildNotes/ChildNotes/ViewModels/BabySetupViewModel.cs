using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class BabySetupViewModel : ViewModelBase
{
    private readonly BabyService _babyService = ServiceProvider.Instance.BabyService;
    private readonly LocaleManager _locale = LocaleManager.Instance;

    // 字段顺序对齐小程序 baby-setup: gender / name / birthDate
    [ObservableProperty] private string _gender = "boy";
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private DateTime? _birthDate;
    [ObservableProperty] private bool _saving;

    public event Action? Completed;

    public void Reset()
    {
        Gender = "boy";
        Name = string.Empty;
        BirthDate = null;
        ErrorMessage = string.Empty;
        Saving = false;
    }

    public void SelectGender(string gender) => Gender = gender;

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = _locale.GetString("BabySetup_ErrName", "请输入宝宝姓名");
            return;
        }
        if (BirthDate is null)
        {
            ErrorMessage = _locale.GetString("BabySetup_ErrBirthday", "请选择出生日期");
            return;
        }

        Saving = true;
        try
        {
            // 统一转 Local Kind，避免 CalendarDatePicker 回传 Unspecified Kind
            // 在后续与 DateTime.Today 比较时抛 DateTimeKind 异常
            _babyService.AddBaby(Name.Trim(), Gender, DateTime.SpecifyKind(BirthDate.Value.Date, DateTimeKind.Local));
            Completed?.Invoke();
        }
        finally
        {
            Saving = false;
        }
    }

    [RelayCommand]
    private void Skip()
    {
        Completed?.Invoke();
    }
}
