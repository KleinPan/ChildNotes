using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.ViewModels;

public partial class GrowthViewModel : ViewModelBase, IActivatable
{
    private readonly MilestoneRepository _milestoneRepo = ServiceProvider.Instance.MilestoneRepository;

    public ObservableCollection<MilestoneDisplayItem> Milestones { get; } = new();
    public MilestoneEditViewModel MilestoneEdit { get; } = new();

    public GrowthViewModel()
    {
        MilestoneEdit.Saved += OnMilestoneSaved;
    }

    public void Activate()
    {
        _ = LoadDataAsync();
    }

    /// <summary>
    /// 异步加载数据：DB 查询移到后台线程，避免切换 tab 时阻塞 UI。
    /// </summary>
    private async Task LoadDataAsync()
    {
        var state = ServiceProvider.Instance.AppState;
        var babyId = state.CurrentBabyId;
        var userId = state.UserId;
        // 后台线程执行 DB 查询
        var list = await Task.Run(() => _milestoneRepo.GetAll(userId, babyId)
                                                  .OrderBy(x => x.RecordDate)
                                                  .ThenBy(x => x.Id)
                                                  .ToList());
        Milestones.Clear();
        foreach (var m in list)
        {
            Milestones.Add(new MilestoneDisplayItem(m));
        }
    }

    [RelayCommand]
    private void AddMilestone()
    {
        MilestoneEdit.InitForAdd();
        MilestoneEdit.Show();
    }

    public void EditMilestone(MilestoneDisplayItem item)
    {
        MilestoneEdit.InitForEdit(item.Milestone);
        MilestoneEdit.Show();
    }

    public void DeleteMilestone(MilestoneDisplayItem item)
    {
        _milestoneRepo.Delete(item.Milestone.Id);
        _ = LoadDataAsync();
    }

    public void OnMilestoneSaved()
    {
        _ = LoadDataAsync();
    }
}

public sealed class MilestoneDisplayItem
{
    public Milestone Milestone { get; }
    public string DateText => ServiceProvider.Instance.DateTimeFormatter.FormatDate(Milestone.RecordDate);
    public string Title => Milestone.Title;
    public string? Content => Milestone.Content;
    public List<string> Photos => string.IsNullOrEmpty(Milestone.PhotosJson)
        ? new()
        : JsonSerializer.Deserialize<List<string>>(Milestone.PhotosJson) ?? new();
    public bool HasPhotos => Photos.Count > 0;
    /// <summary>卡片缩略图最多展示 4 张（与表单上限对齐）。</summary>
    public List<string> PhotoThumbnails => Photos.Take(4).ToList();

    public MilestoneDisplayItem(Milestone m) => Milestone = m;
}
