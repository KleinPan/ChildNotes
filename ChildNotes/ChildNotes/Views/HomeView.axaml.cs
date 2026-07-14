using Avalonia.Controls;
using Avalonia.Data.Converters;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        DevLogger.Log("HomeView", "ctor start");
        InitializeComponent();
        DevLogger.Log("HomeView", "ctor InitializeComponent done");
    }

    // 疫苗追踪卡展开/收起按钮文案
    public static readonly IValueConverter ExpandTextConverter = new FuncValueConverter<bool, string>(
        isExpanded => isExpanded
            ? LocaleManager.Instance.GetString("Common_Collapse", "收起")
            : LocaleManager.Instance.GetString("Common_Expand", "展开"));

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DevLogger.Log("HomeView", "OnAttachedToVisualTree");
    }
}
