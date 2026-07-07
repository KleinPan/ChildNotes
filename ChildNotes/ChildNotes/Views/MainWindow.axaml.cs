using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ChildNotes.Infrastructure;

namespace ChildNotes.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // 暴露 TopLevel 给 ServiceProvider，供 ViewModel 获取 Clipboard 等平台能力
        ServiceProvider.Instance.MainView = this;
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (Application.Current is App app && app.HandleSystemBack())
            {
                e.Handled = true;
            }
        }
    }
}