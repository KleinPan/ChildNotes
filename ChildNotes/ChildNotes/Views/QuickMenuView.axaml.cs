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

    /// <summary>
    /// FAB 可见性 bool → Opacity double 转换器。
    /// true→1（完全显示），false→0（完全透明，配合 IsHitTestVisible=false 防误触）。
    /// 配合 Border.Transitions 的 DoubleTransition 实现 200ms 淡入淡出。
    /// </summary>
    public static readonly IValueConverter FabOpacityConverter = new FuncValueConverter<bool, double>(
        visible => visible ? 1.0 : 0.0);

    /// <summary>点击遮罩关闭菜单</summary>
    private void OnMaskPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is QuickMenuViewModel vm)
        {
            vm.CloseMenuCommand.Execute(null);
        }
    }
}
