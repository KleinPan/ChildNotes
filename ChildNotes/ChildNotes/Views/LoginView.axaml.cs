using Avalonia.Controls;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    public void Bind(LoginViewModel vm)
    {
        DataContext = vm;
    }
}
