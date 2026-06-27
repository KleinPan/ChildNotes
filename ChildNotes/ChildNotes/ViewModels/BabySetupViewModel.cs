using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class BabySetupViewModel : ViewModelBase
{
    private readonly BabyService _babyService = ServiceProvider.Instance.BabyService;

    // 字段顺序对齐小程序 baby-setup: gender / name / birthDate
    [ObservableProperty] private string _gender = "boy";
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private DateTime? _birthDate;
    [ObservableProperty] private bool _saving;
    [ObservableProperty] private string _errorMessage = string.Empty;

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
            ErrorMessage = "请输入宝宝姓名";
            return;
        }
        if (BirthDate is null)
        {
            ErrorMessage = "请选择出生日期";
            return;
        }

        Saving = true;
        try
        {
            _babyService.AddBaby(Name.Trim(), Gender, BirthDate.Value.Date);
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
