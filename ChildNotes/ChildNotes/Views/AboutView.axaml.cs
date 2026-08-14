using Avalonia.Controls;
using Avalonia.Input;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

/// <summary>"关于"页 code-behind：PointerPressed → ViewModel 命令。</summary>
public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
    }

    private void OnUserAgreementTap(object? sender, PointerPressedEventArgs e)
        => (DataContext as AboutViewModel)?.OpenUserAgreementCommand.Execute(null);

    private void OnPrivacyPolicyTap(object? sender, PointerPressedEventArgs e)
        => (DataContext as AboutViewModel)?.OpenPrivacyPolicyCommand.Execute(null);
}
