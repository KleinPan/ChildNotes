using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class MilestoneEditViewModel : ViewModelBase
{
    private readonly MilestoneRepository _repo = ServiceProvider.Instance.MilestoneRepository;
    private readonly AppState _state = ServiceProvider.Instance.AppState;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private DateTimeOffset _recordDate = DateTimeOffset.Now;
    [ObservableProperty] private string _sheetTitle = "添加成长时刻";
    [ObservableProperty] private bool _isVisible;

    private long _editingId;

    public event Action? Saved;

    public void InitForAdd()
    {
        _editingId = 0;
        Title = string.Empty;
        Content = string.Empty;
        var localDate = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local);
        RecordDate = new DateTimeOffset(localDate);
        SheetTitle = "添加成长时刻";
        ErrorMessage = string.Empty;
    }

    public void InitForEdit(Milestone m)
    {
        _editingId = m.Id;
        Title = m.Title;
        Content = m.Content ?? string.Empty;
        var localDate = DateTime.SpecifyKind(m.RecordDate.Date, DateTimeKind.Local);
        RecordDate = new DateTimeOffset(localDate);
        SheetTitle = "编辑成长时刻";
        ErrorMessage = string.Empty;
    }

    public void Show() => IsVisible = true;

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "请输入标题";
            return;
        }
        if (RecordDate.LocalDateTime.Date > DateTime.Today)
        {
            ErrorMessage = "日期不能晚于今天";
            return;
        }

        var m = new Milestone
        {
            Id = _editingId,
            UserId = _state.UserId,
            BabyId = _state.CurrentBabyId,
            Title = Title.Trim(),
            Content = string.IsNullOrWhiteSpace(Content) ? null : Content.Trim(),
            RecordDate = RecordDate.LocalDateTime.Date,
            PhotosJson = "[]",
        };

        if (_editingId == 0)
            _repo.Insert(m);
        else
            _repo.Update(m);

        IsVisible = false;
        Saved?.Invoke();
    }

    [RelayCommand]
    private void Delete()
    {
        if (_editingId == 0) return;
        _repo.Delete(_editingId);
        IsVisible = false;
        Saved?.Invoke();
    }
}
