using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class BabyManagerViewModel : ViewModelBase
{
    private readonly BabyService _babyService = ServiceProvider.Instance.BabyService;
    private readonly AppState _state = ServiceProvider.Instance.AppState;

    public ObservableCollection<Baby> BabyList { get; } = new();

    [ObservableProperty] private bool _hasBaby;
    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private bool _isEditing;          // true=编辑, false=新增
    [ObservableProperty] private bool _isDeleteConfirmOpen;
    [ObservableProperty] private string _title = "宝宝管理";
    [ObservableProperty] private string _editorTitle = "添加宝宝";

    // 编辑表单字段
    [ObservableProperty] private long _editingId;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _gender = "boy";
    [ObservableProperty] private DateTime? _birthDate;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _deleteConfirmName = string.Empty;

    public event Action? BackRequested;
    public event Action? BabyChanged;        // 增删改后通知外部刷新

    public void Load()
    {
        BabyList.Clear();
        var list = _babyService.LoadBabyList();
        foreach (var b in list) BabyList.Add(b);
        HasBaby = list.Count > 0;
    }

    public bool IsCurrentBaby(Baby baby) => _state.CurrentBaby?.Id == baby.Id;

    [RelayCommand]
    private void Back()
    {
        BackRequested?.Invoke();
    }

    public void OpenAdd()
    {
        IsEditing = false;
        EditorTitle = "添加宝宝";
        EditingId = 0;
        Name = string.Empty;
        Gender = "boy";
        BirthDate = null;
        ErrorMessage = string.Empty;
        IsEditorOpen = true;
    }

    public void OpenEdit(Baby baby)
    {
        IsEditing = true;
        EditorTitle = "编辑宝宝";
        EditingId = baby.Id;
        Name = baby.Name;
        Gender = baby.Gender;
        BirthDate = baby.BirthDate?.Date;
        ErrorMessage = string.Empty;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void CloseEditor()
    {
        IsEditorOpen = false;
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

        if (IsEditing)
        {
            var baby = _state.BabyList.FirstOrDefault(b => b.Id == EditingId);
            if (baby is not null)
            {
                baby.Name = Name.Trim();
                baby.Gender = Gender;
                baby.BirthDate = BirthDate.Value.Date;
                _babyService.UpdateBaby(baby);
            }
        }
        else
        {
            _babyService.AddBaby(Name.Trim(), Gender, BirthDate.Value.Date);
        }

        IsEditorOpen = false;
        Load();
        BabyChanged?.Invoke();
    }

    public void OpenDeleteConfirm(Baby baby)
    {
        DeleteConfirmName = baby.Name;
        EditingId = baby.Id;
        IsDeleteConfirmOpen = true;
    }

    [RelayCommand]
    private void CloseDeleteConfirm()
    {
        IsDeleteConfirmOpen = false;
    }

    [RelayCommand]
    private void ConfirmDelete()
    {
        _babyService.DeleteBaby(EditingId);
        IsDeleteConfirmOpen = false;
        Load();
        BabyChanged?.Invoke();
    }
}
