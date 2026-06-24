using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class BabySetupViewModel : ViewModelBase
{
    private readonly BabyService _babyService = ServiceProvider.Instance.BabyService;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _gender = "boy";
    [ObservableProperty] private DateTimeOffset? _birthDate;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private long _editingId;
    [ObservableProperty] private string _title = "添加宝宝";

    public event Action? Completed;

    public void InitForAdd()
    {
        IsEditing = false;
        Title = "添加宝宝";
        Name = string.Empty;
        Gender = "boy";
        BirthDate = null;
        ErrorMessage = string.Empty;
    }

    public void InitForEdit(Baby baby)
    {
        IsEditing = true;
        Title = "编辑宝宝";
        EditingId = baby.Id;
        Name = baby.Name;
        Gender = baby.Gender;
        BirthDate = baby.BirthDate.HasValue ? new DateTimeOffset(baby.BirthDate.Value) : null;
        ErrorMessage = string.Empty;
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

        var birth = BirthDate.Value.Date;
        if (IsEditing)
        {
            var baby = ServiceProvider.Instance.AppState.BabyList.FirstOrDefault(b => b.Id == EditingId);
            if (baby is not null)
            {
                baby.Name = Name.Trim();
                baby.Gender = Gender;
                baby.BirthDate = birth;
                _babyService.UpdateBaby(baby);
            }
        }
        else
        {
            _babyService.AddBaby(Name.Trim(), Gender, birth);
        }
        Completed?.Invoke();
    }

    [RelayCommand]
    private void Skip()
    {
        Completed?.Invoke();
    }
}
