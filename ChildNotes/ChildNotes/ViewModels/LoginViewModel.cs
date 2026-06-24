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
        var result = IsRegisterMode
            ? _auth.Register(Username, Password, NickName)
            : _auth.Login(Username, Password);

        if (result.Success)
        {
            ServiceProvider.Instance.BindUserToState();
            ServiceProvider.Instance.BabyService.LoadBabyList();
            LoginSucceeded?.Invoke();
        }
        else
        {
            ErrorMessage = result.Message;
        }
    }
}
