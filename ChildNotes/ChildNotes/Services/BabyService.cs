using ChildNotes.Data.Repositories;
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

    public List<Baby> LoadBabyList()
    {
        _state.BabyList = _repo.GetByUser(_state.UserId);
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
        return baby;
    }

    public void UpdateBaby(Baby baby)
    {
        _repo.Update(baby);
        var idx = _state.BabyList.FindIndex(b => b.Id == baby.Id);
        if (idx >= 0) _state.BabyList[idx] = baby;
    }

    public void SwitchBaby(long babyId)
    {
        _state.CurrentBaby = _state.BabyList.FirstOrDefault(b => b.Id == babyId);
    }

    public List<BabyMember> GetMembers() => _state.CurrentBaby is null
        ? new()
        : _repo.GetMembers(_state.CurrentBaby.Id);

    public void AddMember(string roleCode, string roleName, string nickName)
    {
        if (_state.CurrentBaby is null) return;

        // 检查当前用户是否已是成员（owner 或其他角色）
        var existingMembers = _repo.GetMembers(_state.CurrentBaby.Id);
        var myMembership = existingMembers.FirstOrDefault(m => m.UserId == _state.UserId);

        if (myMembership is not null)
        {
            // 当前用户已是成员：如果新角色与当前不同，且不是 owner，则更新角色
            // owner 不允许通过 AddMember 修改角色
            if (!myMembership.IsOwner && myMembership.RoleCode != roleCode)
            {
                _repo.UpdateMemberRole(myMembership.Id, roleCode, roleName);
            }
            return;
        }

        // 当前用户还不是成员，直接添加
        var member = new BabyMember
        {
            BabyId = _state.CurrentBaby.Id,
            UserId = _state.UserId,
            RoleCode = roleCode,
            RoleName = roleName,
            IsOwner = false,
            Status = "active",
        };
        _repo.InsertMember(member);
    }

    public void UpdateMemberRole(long memberId, string roleCode, string roleName)
    {
        _repo.UpdateMemberRole(memberId, roleCode, roleName);
    }

    public void RemoveMember(long memberId)
    {
        _repo.DeleteMember(memberId);
    }

    public void EnsureOwnerMember()
    {
        if (_state.CurrentBaby is null) return;
        // 根据当前用户性别推断角色，避免硬编码 "father"
        var (roleCode, roleName) = _state.User?.Gender switch
        {
            1 => ("father", "爸爸"),
            2 => ("mother", "妈妈"),
            _ => ("father", "爸爸"),
        };
        _repo.EnsureOwnerMember(_state.CurrentBaby.Id, _state.UserId, roleCode, roleName);
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
