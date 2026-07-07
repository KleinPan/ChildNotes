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

    /// <summary>
    /// 应用版本号（从程序集 InformationalVersion 读取；CI 构建时由 release workflow 用 tag 名覆盖版本号）
    /// </summary>
    public string AppVersion
    {
        get
        {
            var attr = (System.Reflection.AssemblyInformationalVersionAttribute[])
                System.Attribute.GetCustomAttributes(
                    System.Reflection.Assembly.GetExecutingAssembly(),
                    typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            var ver = attr.Length > 0 ? attr[0].InformationalVersion : "0.0.0";
            return $"v{ver}";
        }
    }

    public ObservableCollection<Baby> BabyList { get; } = new();

    public event Action? LogoutRequested;

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
        LogoutRequested?.Invoke();
    }
}
