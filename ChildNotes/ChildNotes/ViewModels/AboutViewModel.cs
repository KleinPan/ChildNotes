using CommunityToolkit.Mvvm.Input;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// "关于"页 ViewModel：从"我的"页"关于宝宝日记"入口进入。
/// 展示应用名 + 版本号，提供用户协议/隐私政策入口。
/// 子页跳转通过事件回调 MainShellViewModel 执行。
/// </summary>
public partial class AboutViewModel : ViewModelBase
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    public event Action? OpenUserAgreementRequested;
    public event Action? OpenPrivacyPolicyRequested;

    public AboutViewModel()
    {
        Title = _locale.GetString("About_Title", "关于宝宝日记");
    }

    /// <summary>应用版本号（从程序集 InformationalVersion 读取，与 MineViewModel.AppVersion 逻辑一致）。</summary>
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

    /// <summary>应用显示名（供关于页顶部展示）。</summary>
    public string AppName => _locale.GetString("About_AppName", "宝宝日记");

    [RelayCommand] private void OpenUserAgreement() => OpenUserAgreementRequested?.Invoke();
    [RelayCommand] private void OpenPrivacyPolicy() => OpenPrivacyPolicyRequested?.Invoke();
}
