using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class QuickMenuView : UserControl
{
    public QuickMenuView()
    {
        InitializeComponent();
    }

    /// <summary>+ 按钮图标：菜单关闭时显示 "+"，打开时显示 "×"</summary>
    public static readonly IValueConverter FabIconConverter = new FuncValueConverter<bool, string>(
        isOpen => isOpen ? "×" : "+");

    /// <summary>点击遮罩关闭菜单</summary>
    private void OnMaskPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is QuickMenuViewModel vm)
        {
            vm.CloseMenuCommand.Execute(null);
        }
    }
}
