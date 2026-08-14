using Avalonia.Controls;
using Avalonia.Input;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

/// <summary>"应用设置"页 code-behind：PointerPressed → ViewModel 命令。</summary>
public partial class AppSettingsView : UserControl
{
    public AppSettingsView()
    {
        InitializeComponent();
    }

    private void OnLanguageTap(object? sender, PointerPressedEventArgs e)
        => (DataContext as AppSettingsViewModel)?.OpenLanguageCommand.Execute(null);

    private void OnAiSettingsTap(object? sender, PointerPressedEventArgs e)
        => (DataContext as AppSettingsViewModel)?.OpenAiSettingsCommand.Execute(null);

    private void OnReminderTap(object? sender, PointerPressedEventArgs e)
        => (DataContext as AppSettingsViewModel)?.OpenReminderCommand.Execute(null);
}
