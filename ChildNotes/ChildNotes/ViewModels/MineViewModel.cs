using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class MineViewModel : ViewModelBase, IActivatable
{
    private readonly AuthService _auth = ServiceProvider.Instance.AuthService;
    private readonly BabyService _babyService = ServiceProvider.Instance.BabyService;

    [ObservableProperty] private string _nickName = string.Empty;
    [ObservableProperty] private string _avatarUrl = string.Empty;
    [ObservableProperty] private string _roleText = "家长";
    [ObservableProperty] private int _babyCount;

    public ObservableCollection<Baby> BabyList { get; } = new();

    public void Activate()
    {
        var user = _auth.CurrentUser;
        NickName = user?.NickName ?? "未登录";
        AvatarUrl = user?.AvatarUrl ?? string.Empty;

        BabyList.Clear();
        var babies = _babyService.LoadBabyList();
        foreach (var b in babies) BabyList.Add(b);
        BabyCount = babies.Count;
    }

    [RelayCommand]
    private void Logout()
    {
        _auth.Logout();
    }
}
