using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// 语言设置页 ViewModel：提供中英文切换。
/// 切换后立即热刷新 UI，并持久化到本地文件（重启后生效）。
/// </summary>
public partial class LanguageSettingsViewModel : ViewModelBase
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    public LanguageSettingsViewModel()
    {
        Title = _locale.GetString("Language_Title", "语言");
    }

    /// <summary>当前是否选中简体中文。</summary>
    public bool IsZhHans => _locale.CurrentLanguage == AppLanguage.ZhHans;

    /// <summary>当前是否选中英文。</summary>
    public bool IsEn => _locale.CurrentLanguage == AppLanguage.En;

    [RelayCommand]
    private void SelectZhHans()
    {
        if (_locale.CurrentLanguage == AppLanguage.ZhHans) return;
        _locale.SetLanguage(AppLanguage.ZhHans);
        RefreshSelection();
    }

    [RelayCommand]
    private void SelectEn()
    {
        if (_locale.CurrentLanguage == AppLanguage.En) return;
        _locale.SetLanguage(AppLanguage.En);
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        OnPropertyChanged(nameof(IsZhHans));
        OnPropertyChanged(nameof(IsEn));
    }
}
