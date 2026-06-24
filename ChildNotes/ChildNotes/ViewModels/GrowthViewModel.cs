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
        LoadData();
    }

    private void LoadData()
    {
        var state = ServiceProvider.Instance.AppState;
        Milestones.Clear();
        var list = _milestoneRepo.GetAll(state.UserId, state.CurrentBabyId);
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
        LoadData();
    }

    public void OnMilestoneSaved()
    {
        LoadData();
    }
}

public sealed class MilestoneDisplayItem
{
    public Milestone Milestone { get; }
    public string DateText => Milestone.RecordDate.ToString("yyyy-MM-dd");
    public string Title => Milestone.Title;
    public string? Content => Milestone.Content;
    public List<string> Photos => string.IsNullOrEmpty(Milestone.PhotosJson)
        ? new()
        : JsonSerializer.Deserialize<List<string>>(Milestone.PhotosJson) ?? new();

    public MilestoneDisplayItem(Milestone m) => Milestone = m;
}
