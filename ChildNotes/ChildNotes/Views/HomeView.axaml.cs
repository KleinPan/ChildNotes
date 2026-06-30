using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ChildNotes.Infrastructure;
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

    // 疫苗/活动追踪卡展开/收起按钮文案
    public static readonly IValueConverter ExpandTextConverter = new FuncValueConverter<bool, string>(
        isExpanded => isExpanded ? "收起" : "展开");

    // 活动类别 → 胶囊背景色（对齐小程序 at-cat：indoor #f0f3fa / outdoor #fff7f0）
    public static readonly IValueConverter ActivityCategoryBgConverter = new FuncValueConverter<string?, IBrush?>(
        category => category == "outdoor" ? Brush.Parse("#FFF7F0") : Brush.Parse("#F0F3FA"));

    // 活动类别 → 胶囊文字色（对齐小程序 at-cat：indoor #576b95 / outdoor #e67e22）
    public static readonly IValueConverter ActivityCategoryFgConverter = new FuncValueConverter<string?, IBrush?>(
        category => category == "outdoor" ? Brush.Parse("#E67E22") : Brush.Parse("#576B95"));

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DevLogger.Log("HomeView", "OnAttachedToVisualTree");
    }
}
