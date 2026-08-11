using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.Services;

public sealed class BabyService
{
    private readonly BabyRepository _repo;
    private readonly AppState _state;

    public BabyService(BabyRepository repo, AppState state)
    {
        _repo = repo;
        _state = state;
    }

    /// <summary>由 ServiceProvider 在构造完成后注入，避免循环依赖。</summary>
    public SyncTrigger? SyncTrigger { get; set; }

    private void NotifyWrite()
    {
        try { SyncTrigger?.NotifyWrite(); } catch { }
    }

    public IReadOnlyList<Baby> LoadBabyList()
    {
        DevLogger.Log("Baby", $"LoadBabyList start, userId={_state.UserId}");
        try
        {
            _state.BabyList.Clear();
            foreach (var b in _repo.GetByUser(_state.UserId)) _state.BabyList.Add(b);
            DevLogger.Log("Baby", $"LoadBabyList: count={_state.BabyList.Count}");
            if (_state.BabyList.Count > 0)
            {
                _state.CurrentBaby ??= _state.BabyList[0];
            }
            else
            {
                _state.CurrentBaby = null;
            }
            return _state.BabyList;
        }
        catch (Exception ex)
        {
            DevLogger.Log("Baby", ex);
            throw;
        }
    }

    public Baby AddBaby(string name, string gender, DateTime? birthDate, string avatar = "")
    {
        var baby = new Baby
        {
            UserId = _state.UserId,
            Name = name,
            Gender = gender,
            BirthDate = birthDate,
            Avatar = avatar,
        };
        baby.Id = _repo.Insert(baby);
        _state.BabyList.Add(baby);
        _state.CurrentBaby = baby;
        NotifyWrite();
        return baby;
    }

    public void UpdateBaby(Baby baby)
    {
        _repo.Update(baby);
        var idx = IndexOfBaby(baby.Id);
        if (idx >= 0) _state.BabyList[idx] = baby;
        if (_state.CurrentBaby?.Id == baby.Id) _state.CurrentBaby = baby;
        NotifyWrite();
    }

    public void DeleteBaby(string babyId)
    {
        _repo.Delete(babyId);
        var idx = IndexOfBaby(babyId);
        if (idx < 0) return;
        _state.BabyList.RemoveAt(idx);
        if (_state.CurrentBaby?.Id == babyId)
        {
            _state.CurrentBaby = _state.BabyList.Count > 0 ? _state.BabyList[0] : null;
        }
        NotifyWrite();
    }

    /// <summary>查找宝宝在 ObservableCollection 中的索引（ObservableCollection 无 FindIndex）。</summary>
    private int IndexOfBaby(string id)
    {
        for (int i = 0; i < _state.BabyList.Count; i++)
            if (_state.BabyList[i].Id == id) return i;
        return -1;
    }

    public void SwitchBaby(string babyId)
    {
        _state.CurrentBaby = _state.BabyList.FirstOrDefault(b => b.Id == babyId);
    }

    public string GetGrowthStageText()
    {
        if (_state.CurrentBaby?.BirthDate is not { } birth) return string.Empty;
        var days = (DateTime.Today - birth).Days;
        return days switch
        {
            < 28 => "新生儿期",
            < 365 => "婴儿期",
            < 365 * 3 => "幼儿期",
            _ => "学龄前期",
        };
    }
}
