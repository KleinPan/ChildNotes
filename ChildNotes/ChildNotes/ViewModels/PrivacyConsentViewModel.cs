using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// 隐私协议弹窗 ViewModel。
///
/// 展示时机：首次启动 / 协议版本升级时，由 App.axaml.cs 在恢复会话前调用。
/// 选项：
/// - "同意并继续" → 调 PrivacyConsent.Agree() 持久化 → 触发 ConsentGiven 事件，App 继续启动
/// - "不同意" → 触发 Disagreed 事件，App 调 IApplicationExit.Exit() 退出
/// - "查看完整协议" → 弹出完整协议内容（从 Assets/PrivacyPolicy.md 加载）
/// </summary>
public partial class PrivacyConsentViewModel : ViewModelBase
{
    /// <summary>用户同意当前版本协议。</summary>
    public event Action? ConsentGiven;

    /// <summary>用户不同意，请求退出应用。</summary>
    public event Action? Disagreed;

    /// <summary>协议摘要文本（弹窗首屏展示）。</summary>
    [ObservableProperty] private string _summaryText =
        "感谢您使用 ChildNotes。本应用尊重并保护您的隐私，" +
        "在您使用前请仔细阅读《隐私政策》。\n\n" +
        "• 数据默认存储在设备本地\n" +
        "• 仅在您主动启用同步时上传至您的服务器\n" +
        "• 不收集设备识别码、位置等敏感信息\n" +
        "• 日志已自动脱敏处理";

    /// <summary>完整协议文本（点击"查看完整协议"后加载）。</summary>
    [ObservableProperty] private string _fullPolicyText = string.Empty;

    /// <summary>是否展示完整协议视图（替代摘要视图）。</summary>
    [ObservableProperty] private bool _showFullPolicy;

    /// <summary>是否为只读模式（从"我的"页打开查看，不展示同意/不同意按钮）。</summary>
    [ObservableProperty] private bool _isReadOnly;

    public PrivacyConsentViewModel()
    {
        Title = "隐私政策";
    }

    /// <summary>同意并继续：持久化同意状态，触发后续启动流程。</summary>
    [RelayCommand]
    private void Agree()
    {
        PrivacyConsent.Agree();
        ConsentGiven?.Invoke();
    }

    /// <summary>不同意：触发退出事件（仅首次启动模式有效）。</summary>
    [RelayCommand]
    private void Disagree()
    {
        Disagreed?.Invoke();
    }

    /// <summary>关闭弹窗（只读模式下由"关闭"按钮调用）。</summary>
    [RelayCommand]
    private void Close()
    {
        ConsentGiven?.Invoke();
    }

    /// <summary>查看完整协议：从 Assets 加载 markdown 文本并切换视图。</summary>
    [RelayCommand]
    private void ViewFullPolicy()
    {
        if (string.IsNullOrEmpty(FullPolicyText))
        {
            FullPolicyText = LoadEmbeddedPolicy();
        }
        ShowFullPolicy = true;
    }

    /// <summary>从完整协议视图返回摘要。</summary>
    [RelayCommand]
    private void BackToSummary()
    {
        ShowFullPolicy = false;
    }

    /// <summary>从 Assets/PrivacyPolicy.md 加载协议正文。失败时返回占位文本。</summary>
    private static string LoadEmbeddedPolicy()
    {
        try
        {
            // Avalonia 12 提供静态 AssetLoader 类，直接 Open avares:// 资源
            var assetUri = new Uri("avares://ChildNotes/Assets/PrivacyPolicy.md");
            using var stream = global::Avalonia.Platform.AssetLoader.Open(assetUri);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            DevLogger.Log("Privacy", $"LoadEmbeddedPolicy failed: {ex.Message}");
        }

        return "未能加载完整协议正文。请稍后重试或联系开发者。";
    }
}
