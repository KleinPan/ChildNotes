using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.VisualTree;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

/// <summary>
/// 导出中状态 → 按钮文案：true=导出中…，false=导出。
/// </summary>
public class ExportingTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is true ? "导出中…" : "导出";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public partial class DeveloperOptionsView : UserControl
{
    /// <summary>静态引用，供 XAML 绑定使用。</summary>
    public static readonly ExportingTextConverter ExportingTextConverter = new();

    public DeveloperOptionsView()
    {
        InitializeComponent();
    }

    private void OnAppLogTap(object? sender, PointerPressedEventArgs e)
    {
        // 委托给当前 ViewModel 的命令（避免直接找 MainShell，DeveloperOptions 嵌套较深）
        if (DataContext is DeveloperOptionsViewModel vm)
        {
            vm.OpenAppLogCommand.Execute(null);
        }
        else if (FindShell() is { } shell)
        {
            shell.OpenAppLog();
        }
    }

    private MainShellViewModel? FindShell()
    {
        var node = this.FindAncestorOfType<UserControl>();
        while (node is not null && node.DataContext is not MainShellViewModel)
        {
            node = node.FindAncestorOfType<UserControl>();
        }
        return node?.DataContext as MainShellViewModel;
    }
}
