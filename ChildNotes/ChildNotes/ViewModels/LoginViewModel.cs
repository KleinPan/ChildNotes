using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly AuthService _auth = ServiceProvider.Instance.AuthService;
    private readonly Data.Repositories.SyncConfigRepository _cfgRepo = ServiceProvider.Instance.SyncConfigRepository;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _nickName = string.Empty;
    [ObservableProperty] private bool _isRegisterMode;

    public event Action? LoginSucceeded;

    [RelayCommand]
    private void ToggleMode()
    {
        IsRegisterMode = !IsRegisterMode;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task Submit()
    {
        ErrorMessage = string.Empty;
        try
        {
            DevLogger.Log("Login", $"Submit start: Mode={IsRegisterMode}, User='{Username}', PwdLen={Password?.Length ?? 0}, Nick='{NickName}'");
            // PBKDF2 哈希 + DB 查询放后台线程，避免阻塞 UI 30-80ms
            var result = await Task.Run(() => IsRegisterMode
                ? _auth.Register(Username, Password, NickName)
                : _auth.Login(Username, Password));

            DevLogger.Log("Login", $"Result: Success={result.Success}, Msg='{result.Message}', UserId={result.User?.Id}");

            if (result.Success)
            {
                // 登录/注册成功后把凭据写入 sync_config，供 ApiSyncService 调用 /api/auth/login 取 token。
                // 这样同步页无需再单独输入账号密码，凭据随登录自动同步。
                SaveCredentialsToSyncConfig();
                ServiceProvider.Instance.BindUserToState();
                DevLogger.Log("Login", "BindUserToState done");
                ServiceProvider.Instance.BabyService.LoadBabyList();
                DevLogger.Log("Login", "LoadBabyList done");
                var subscribers = LoginSucceeded?.GetInvocationList()?.Length ?? 0;
                DevLogger.Log("Login", $"LoginSucceeded subscribers={subscribers}");
                // 直接调用 App 静态方法，绕过事件订阅可能丢失的问题（安卓 Activity 重建）
                App.RaiseLoginSucceeded();
                // 兼容备份：如果 App 的订阅还在，也触发事件
                LoginSucceeded?.Invoke();
                DevLogger.Log("Login", "LoginSucceeded invoked");
                // 登录成功后主动触发首次同步，避免等待 8 秒启动定时器或 15 分钟保活
                // fire-and-forget：同步失败不影响登录流程，下次触发会再试
                _ = ServiceProvider.Instance.SyncTrigger.RunNowAsync();
                DevLogger.Log("Login", "Initial sync triggered");
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("Login", ex);
            // 避免被 App.axaml.cs 的全局 UnhandledException 处理器静默吞掉，
            // 让用户在登录页直接看到完整错误（含类型/消息/内层异常），便于安卓真机排查
            var detail = ex.ToString();
            if (ex.InnerException is not null)
                detail += "\n---> " + ex.InnerException;
            ErrorMessage = "操作失败：" + detail;
        }
    }

    /// <summary>
    /// 把当前登录的用户名/明文密码写入 sync_config。
    /// 同步功能（ApiSyncService）会以此凭据向服务器换取 token，
    /// 因此登录后同步页不再需要单独输入账号密码。
    /// </summary>
    private void SaveCredentialsToSyncConfig()
    {
        try
        {
            var cfg = _cfgRepo.Get();
            cfg.Username = Username.Trim();
            cfg.Password = Password;
            // 清空旧 token，避免使用上一账号的失效 token
            cfg.Token = string.Empty;
            _cfgRepo.Save(cfg);
            DevLogger.Log("Login", $"SyncConfig credentials updated: user={cfg.Username}");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Login", "SaveCredentialsToSyncConfig failed: " + ex.Message);
        }
    }
}
