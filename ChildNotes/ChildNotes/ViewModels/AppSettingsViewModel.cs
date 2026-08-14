using CommunityToolkit.Mvvm.Input;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// "应用设置"页 ViewModel：从"我的"页"应用设置"入口进入。
/// 收纳应用配置类入口（语言/AI 设置/提醒设置），本身不含业务逻辑，
/// 子页跳转通过事件回调 MainShellViewModel 执行。
/// </summary>
public partial class AppSettingsViewModel : ViewModelBase
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    public event Action? OpenLanguageRequested;
    public event Action? OpenAiSettingsRequested;
    public event Action? OpenReminderRequested;

    public AppSettingsViewModel()
    {
        Title = _locale.GetString("AppSettings_Title", "应用设置");
        _locale.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>当前语言显示名（语言入口右侧展示）。与 MineViewModel.LanguageDisplayText 逻辑一致。</summary>
    public string LanguageDisplayText => _locale.CurrentLanguage == AppLanguage.En
        ? _locale.GetString("Language_En", "English")
        : _locale.GetString("Language_ZhHans", "简体中文");

    private void OnLanguageChanged(AppLanguage lang) => OnPropertyChanged(nameof(LanguageDisplayText));

    [RelayCommand] private void OpenLanguage() => OpenLanguageRequested?.Invoke();
    [RelayCommand] private void OpenAiSettings() => OpenAiSettingsRequested?.Invoke();
    [RelayCommand] private void OpenReminder() => OpenReminderRequested?.Invoke();
}
