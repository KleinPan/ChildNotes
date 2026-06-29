using System.Collections.ObjectModel;
using ChildNotes.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChildNotes.Services;

public sealed partial class AppState : ObservableObject
{
    [ObservableProperty] private AppUser? _user;
    [ObservableProperty] private Baby? _currentBaby;
    public ObservableCollection<Baby> BabyList { get; } = new();

    public long UserId => User?.Id ?? 0;
    public long? CurrentBabyId => CurrentBaby?.Id;

    public void Clear()
    {
        User = null;
        CurrentBaby = null;
        BabyList.Clear();
    }
}
