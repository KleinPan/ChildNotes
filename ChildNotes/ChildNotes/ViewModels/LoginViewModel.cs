using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly AuthService _auth = ServiceProvider.Instance.AuthService;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _nickName = string.Empty;
    [ObservableProperty] private bool _isRegisterMode;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public event Action? LoginSucceeded;

    [RelayCommand]
    private void ToggleMode()
    {
        IsRegisterMode = !IsRegisterMode;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void Submit()
    {
        ErrorMessage = string.Empty;
        try
        {
            DevLogger.Log("Login", $"Submit start: Mode={IsRegisterMode}, User='{Username}', PwdLen={Password?.Length ?? 0}, Nick='{NickName}'");
            var result = IsRegisterMode
                ? _auth.Register(Username, Password, NickName)
                : _auth.Login(Username, Password);

            DevLogger.Log("Login", $"Result: Success={result.Success}, Msg='{result.Message}', UserId={result.User?.Id}");

            if (result.Success)
            {
                ServiceProvider.Instance.BindUserToState();
                DevLogger.Log("Login", "BindUserToState done");
                ServiceProvider.Instance.BabyService.LoadBabyList();
                DevLogger.Log("Login", "LoadBabyList done");
                var subscribers = LoginSucceeded?.GetInvocationList()?.Length ?? 0;
                DevLogger.Log("Login", $"Invoking LoginSucceeded, subscribers={subscribers}");
                LoginSucceeded?.Invoke();
                DevLogger.Log("Login", "LoginSucceeded invoked");
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
}
