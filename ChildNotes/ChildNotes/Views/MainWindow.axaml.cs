using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ChildNotes.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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