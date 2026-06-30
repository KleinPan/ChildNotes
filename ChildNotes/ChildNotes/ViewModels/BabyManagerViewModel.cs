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
    [ObservableProperty] private string _editorTitle = "添加宝宝";

    // 编辑表单字段
    [ObservableProperty] private long _editingId;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _gender = "boy";
    [ObservableProperty] private DateTime? _birthDate;
    [ObservableProperty] private string _deleteConfirmName = string.Empty;

    public event Action? BabyChanged;        // 增删改后通知外部刷新

    public void Load()
    {
        BabyList.Clear();
        var list = _babyService.LoadBabyList();
        foreach (var b in list) BabyList.Add(b);
        HasBaby = list.Count > 0;
    }

    /// <summary>
    /// 异步加载：DB 查询放到后台线程，UI 线程仅做集合填充。
    /// 用于弹层"先打开再加载"模式，避免阻塞 UI。
    /// </summary>
    public async Task LoadAsync()
    {
        var list = await Task.Run(() => _babyService.LoadBabyList());
        BabyList.Clear();
        foreach (var b in list) BabyList.Add(b);
        HasBaby = list.Count > 0;
    }

    public bool IsCurrentBaby(Baby baby) => _state.CurrentBaby?.Id == baby.Id;

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
        // 数据库读出的 BirthDate 是 Unspecified Kind，绑定到 CalendarDatePicker 后再回传
        // 与 DateTime.Today 做差值运算会抛 DateTimeKind 异常，显式指定 Local Kind
        BirthDate = baby.BirthDate.HasValue
            ? DateTime.SpecifyKind(baby.BirthDate.Value.Date, DateTimeKind.Local)
            : null;
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
                // 统一转 Local Kind，避免 CalendarDatePicker 回传 Unspecified Kind
                // 在后续与 DateTime.Today 比较时抛 DateTimeKind 异常
                baby.BirthDate = DateTime.SpecifyKind(BirthDate.Value.Date, DateTimeKind.Local);
                _babyService.UpdateBaby(baby);
            }
        }
        else
        {
            _babyService.AddBaby(Name.Trim(), Gender, DateTime.SpecifyKind(BirthDate.Value.Date, DateTimeKind.Local));
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
        IsEditorOpen = false;
        Load();
        BabyChanged?.Invoke();
    }
}
