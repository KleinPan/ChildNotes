using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using System.Globalization;

namespace ChildNotes.Views;

public partial class InAppMessageView : UserControl
{
    public InAppMessageView()
    {
        InitializeComponent();
    }

    /// <summary>已读消息透明度降低：未读=1.0，已读=0.6。</summary>
    public static readonly IValueConverter ReadOpacityConverter =
        new FuncValueConverter<bool, double>(isRead => isRead ? 0.6 : 1.0);

    /// <summary>Count=0 → true（用于空状态展示）。</summary>
    public static readonly IValueConverter ZeroToBoolConverter =
        new FuncValueConverter<int, bool>(count => count == 0);
}
