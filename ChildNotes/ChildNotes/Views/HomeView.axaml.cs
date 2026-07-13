using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using ChildNotes.Infrastructure;
using ChildNotes.Models.Home;
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

    /// <summary>点击活动时间轴卡片上的删除按钮：触发 ViewModel 弹出确认对话框。</summary>
    private void OnActivityDeleteTap(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (sender is Border border && border.Tag is ActivityTimelineItem item && DataContext is HomeViewModel vm)
        {
            vm.ActivityTracking.RequestDeleteActivity(item);
        }
    }

    /// <summary>
    /// 点击活动卡片（非删除按钮区域）：进入编辑。对齐喂养页 OnRecordTap 用 Tapped 而非 PointerPressed，
    /// 避免上下滑动滚动列表时误触。删除按钮的 PointerPressed 已设 e.Handled=true，正常不会触发此 Tapped。
    /// </summary>
    private void OnActivityCardTap(object? sender, TappedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ActivityTimelineItem item) return;
        if (DataContext is not HomeViewModel vm) return;
        vm.ActivityTracking.EditActivity(item);
    }
}
