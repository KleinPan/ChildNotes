using Avalonia.Controls;
using Avalonia.Input;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class LanguageSettingsView : UserControl
{
    public LanguageSettingsView()
    {
        InitializeComponent();
    }

    private void OnSelectZhHans(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is LanguageSettingsViewModel vm) vm.SelectZhHansCommand.Execute(null);
    }

    private void OnSelectEn(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is LanguageSettingsViewModel vm) vm.SelectEnCommand.Execute(null);
    }
}
