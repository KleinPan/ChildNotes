using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// 邮箱验证码登录 ViewModel（v5 重构）。
/// 流程：
///   1) 输入邮箱 → 点"发送验证码"按钮 → SendCodeCommand
///   2) 60 秒倒计时（防止频繁发送）→ 倒计时结束后可重发
///   3) 输入验证码 → 点"登录"按钮 → VerifyCodeCommand
///   4) 验证成功 → App.RaiseLoginSucceeded → 进入主界面 → 触发首次同步
/// 不区分注册/登录：后端 verify-code 自动判断邮箱是否已存在，不存在则自动创建账号。
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly AuthService _auth = ServiceProvider.Instance.AuthService;
    private readonly Data.Repositories.SyncConfigRepository _cfgRepo = ServiceProvider.Instance.SyncConfigRepository;
    private readonly LocaleManager _locale = LocaleManager.Instance;

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private bool _showServerConfig;

    /// <summary>发送验证码倒计时（秒）。> 0 时按钮禁用并显示倒计时文本。</summary>
    [ObservableProperty] private int _countdownSeconds;

    /// <summary>是否正在发送验证码（防重复点击）。</summary>
    [ObservableProperty] private bool _isSendingCode;

    /// <summary>是否正在验证登录。</summary>
    [ObservableProperty] private bool _isVerifying;

    private CancellationTokenSource? _countdownCts;

    public event Action? LoginSucceeded;

    public LoginViewModel()
    {
        // 启动时读取已保存的服务器地址，方便用户确认/修改
        try
        {
            var cfg = _cfgRepo.Get();
            _serverUrl = cfg.ServerUrl ?? string.Empty;
        }
        catch { /* 首次启动表还没建，忽略 */ }
    }

    /// <summary>发送验证码按钮是否可点击。</summary>
    public bool CanSendCode => !IsSendingCode && CountdownSeconds <= 0 && !string.IsNullOrWhiteSpace(Email);

    /// <summary>发送验证码按钮显示文本。</summary>
    public string SendCodeButtonText =>
        CountdownSeconds > 0
            ? string.Format(_locale.GetString("Login_ResendIn", "{0}s 后重发"), CountdownSeconds)
            : _locale.GetString("Login_SendCode", "发送验证码");

    /// <summary>登录按钮是否可点击。</summary>
    public bool CanVerify => !IsVerifying && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Code);

    partial void OnEmailChanged(string value) => OnPropertyChanged(nameof(CanSendCode));
    partial void OnCodeChanged(string value) => OnPropertyChanged(nameof(CanVerify));
    partial void OnCountdownSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(CanSendCode));
        OnPropertyChanged(nameof(SendCodeButtonText));
    }
    partial void OnIsSendingCodeChanged(bool value) => OnPropertyChanged(nameof(CanSendCode));
    partial void OnIsVerifyingChanged(bool value) => OnPropertyChanged(nameof(CanVerify));

    [RelayCommand]
    private void ToggleServerConfig()
    {
        ShowServerConfig = !ShowServerConfig;
    }

    /// <summary>
    /// 发送邮箱验证码。
    /// 成功后启动 60 秒倒计时（防止频繁发送）。
    /// 失败显示错误信息，不启动倒计时（允许立即重试）。
    /// </summary>
    [RelayCommand]
    private async Task SendCode()
    {
        ErrorMessage = string.Empty;
        var trimmed = Email.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || !trimmed.Contains('@'))
        {
            ErrorMessage = _locale.GetString("Login_InvalidEmail", "请输入有效的邮箱");
            return;
        }

        // 先保存服务器地址到 sync_config，确保 AuthService 能拿到地址
        if (!SaveServerUrlToSyncConfig()) return;

        IsSendingCode = true;
        try
        {
            DevLogger.Log("Login", $"SendCode start: email={trimmed}");
            var result = await _auth.SendCodeAsync(trimmed);
            DevLogger.Log("Login", $"SendCode result: success={result.Success}, msg={result.Message}");

            if (result.Success)
            {
                // 启动 60 秒倒计时
                StartCountdown(60);
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("Login", "SendCode exception: " + ex.Message);
            ErrorMessage = string.Format(_locale.GetString("Login_OperationFailed", "操作失败：{0}"), ex.Message);
        }
        finally
        {
            IsSendingCode = false;
        }
    }

    /// <summary>
    /// 验证邮箱验证码并完成登录/自动注册。
    /// 成功 → App.RaiseLoginSucceeded → 进入主界面 → 触发首次同步（Full Pull Only）。
    /// 失败显示错误信息，保留验证码输入（用户可修改后重试）。
    /// </summary>
    [RelayCommand]
    private async Task Verify()
    {
        ErrorMessage = string.Empty;
        var trimmedEmail = Email.Trim();
        var trimmedCode = Code.Trim();
        if (string.IsNullOrWhiteSpace(trimmedEmail) || !trimmedEmail.Contains('@'))
        {
            ErrorMessage = _locale.GetString("Login_InvalidEmail", "请输入有效的邮箱");
            return;
        }
        if (string.IsNullOrWhiteSpace(trimmedCode))
        {
            ErrorMessage = _locale.GetString("Login_InvalidCode", "请输入验证码");
            return;
        }

        IsVerifying = true;
        try
        {
            DevLogger.Log("Login", $"Verify start: email={trimmedEmail}");
            var result = await _auth.VerifyCodeAsync(trimmedEmail, trimmedCode);
            DevLogger.Log("Login", $"Verify result: success={result.Success}, msg={result.Message}, userId={result.User?.Id}, newUser={result.NewUser}");

            if (result.Success)
            {
                ServiceProvider.Instance.BindUserToState();
                DevLogger.Log("Login", "BindUserToState done");
                // 登录成功后确保欢迎消息存在
                ServiceProvider.Instance.InAppMessageService.EnsureWelcomeMessage();
                ServiceProvider.Instance.BabyService.LoadBabyList();
                DevLogger.Log("Login", "LoadBabyList done");

                // 新用户：注入"赠送积分"欢迎消息（首次登录明确提示）
                if (result.NewUser)
                {
                    try
                    {
                        ServiceProvider.Instance.InAppMessageService.Insert(new InAppMessage
                        {
                            Id = $"bonus-{result.User!.Id}",
                            UserId = result.User.Id,
                            Title = _locale.GetString("Login_WelcomeTitle", "🎉 欢迎注册！已赠送 100 积分"),
                            Body = string.Format(_locale.GetString("Login_WelcomeBody", "感谢注册 ChildNotes！系统已自动为您赠送 {0} 积分，可用于 AI 喂养分析等高级功能。去「积分任务」签到还能每日领取积分哦。"), PointsConstants.NewUserBonusPoints),
                            Category = "general",
                            DataJson = "{}",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow.ToString("O"),
                        });
                    }
                    catch (Exception msgEx) { DevLogger.Log("Login", "Insert bonus message failed: " + msgEx.Message); }
                }

                var subscribers = LoginSucceeded?.GetInvocationList()?.Length ?? 0;
                DevLogger.Log("Login", $"LoginSucceeded subscribers={subscribers}");
                // 直接调用 App 静态方法，绕过事件订阅可能丢失的问题（安卓 Activity 重建）
                App.RaiseLoginSucceeded();
                // 兼容备份：如果 App 的订阅还在，也触发事件
                LoginSucceeded?.Invoke();
                DevLogger.Log("Login", "LoginSucceeded invoked");
                // 登录成功后主动触发首次同步（Full Pull Only：LastSyncAt=null 时只 Pull 不 Push）
                // fire-and-forget：同步失败不影响登录流程，下次触发会再试
                _ = ServiceProvider.Instance.SyncTrigger.RunNowAsync();
                DevLogger.Log("Login", "Initial sync triggered");

#if DEV_BUILD
                // 开发版 APK：自动激活永不过期会员（后端需开启 EnableDevAutoActivate）
                DevLogger.Log("Login", "[DevActivate] DEV_BUILD 已定义，准备激活会员");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(500);
                        var ok = await ServiceProvider.Instance.MembershipApiClient.DevActivatePermanentAsync();
                        DevLogger.Log("Login", $"[DevActivate] 结果：{(ok ? "success" : "skipped/failed")}");
                    }
                    catch (Exception exDev) { DevLogger.Log("Login", "[DevActivate] 异常：" + exDev.GetType().Name + ": " + exDev.Message, DevLogger.Level.Error); }
                });
#endif
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("Login", ex);
            var detail = ex.ToString();
            if (ex.InnerException is not null)
                detail += "\n---> " + ex.InnerException;
            ErrorMessage = string.Format(_locale.GetString("Login_OperationFailed", "操作失败：{0}"), detail);
        }
        finally
        {
            IsVerifying = false;
        }
    }

    /// <summary>
    /// 启动倒计时：每秒递减，到 0 时停止并允许重新发送。
    /// </summary>
    private void StartCountdown(int seconds)
    {
        _countdownCts?.Cancel();
        _countdownCts = new CancellationTokenSource();
        var token = _countdownCts.Token;
        CountdownSeconds = seconds;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && CountdownSeconds > 0)
            {
                await Task.Delay(1000, token);
                if (token.IsCancellationRequested) break;
                CountdownSeconds--;
            }
        }, token);
    }

    /// <summary>
    /// 把登录页输入的服务器地址写入 sync_config。
    /// 必须在 SendCode 之前调用，让 AuthService 能拿到地址。
    /// 空值也保存（=纯本地模式，不同步到后端）。
    /// 返回 false 表示地址校验失败（ErrorMessage 已设置）。
    /// </summary>
    private bool SaveServerUrlToSyncConfig()
    {
        try
        {
            var cfg = _cfgRepo.Get();
            var url = (ServerUrl ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(url) && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                                           && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = _locale.GetString("Login_ErrServerUrl", "服务器地址必须以 http:// 或 https:// 开头");
                return false;
            }
            cfg.ServerUrl = url;
            _cfgRepo.Save(cfg);
            DevLogger.Log("Login", $"SyncConfig server url updated: {url}");
            return true;
        }
        catch (Exception ex)
        {
            DevLogger.Log("Login", "SaveServerUrlToSyncConfig failed: " + ex.Message);
            return true; // 不阻断登录流程，AuthService 会用默认地址
        }
    }
}
