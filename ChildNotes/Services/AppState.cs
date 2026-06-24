using ChildNotes.Models;

namespace ChildNotes.Services;

public sealed class AppState
{
    public AppUser? User { get; set; }
    public Baby? CurrentBaby { get; set; }
    public List<Baby> BabyList { get; set; } = new();

    public long UserId => User?.Id ?? 0;
    public long? CurrentBabyId => CurrentBaby?.Id;
}
