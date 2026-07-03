using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Models;
using ChildNotes.Shared.Constants;

namespace ChildNotes.ViewModels;

/// <summary>
/// 首页底部功能面板 ViewModel：管理输入栏下方的图标网格面板展开/收起。
/// 支持分页（每页最多 8 项 = 4 列 × 2 行），项数 >8 时自动分页，
/// 通过 Carousel 左右滑动切换页面，PipsPager 显示页码指示器。
/// </summary>
public partial class QuickMenuViewModel : ViewModelBase
{
    /// <summary>每页最多项数（4 列 × 2 行）</summary>
    private const int PageSize = 8;

    [ObservableProperty] private bool _isMenuOpen;
    [ObservableProperty] private int _currentPage;

    /// <summary>
    /// 底部记录抽屉是否打开（由 MainShellViewModel 同步镜像）。
    /// 抽屉打开期间功能面板必须隐藏，避免遮挡表单。
    /// </summary>
    [ObservableProperty] private bool _isRecordSheetOpen;

    /// <summary>请求主壳层打开记录表单（由 MainShellViewModel 订阅）。</summary>
    public event Action<string>? OpenRecordRequested;

    /// <summary>所有功能项（不分页前的完整列表）</summary>
    public ObservableCollection<QuickActionItem> Actions { get; } = new();

    /// <summary>分页后的页面列表，每页最多 8 项。Carousel 绑定此属性。</summary>
    public ObservableCollection<List<QuickActionItem>> Pages { get; } = new();

    /// <summary>总页数（PipsPager Count 绑定）</summary>
    public int PageCount => Pages.Count;

    /// <summary>是否显示页码指示器（仅 >1 页时显示）</summary>
    public bool HasMultiplePages => Pages.Count > 1;

    public QuickMenuViewModel()
    {
        // 8 项：常用在下排（Row=2），不常用在上排（Row=1）
        // Ai 记已迁移到首页底部输入栏
        // 异常记录已在首页有独立红色入口
        Actions.Add(new QuickActionItem("📏", "成长", RecordType.Growth, "#E0F2F1"));
        Actions.Add(new QuickActionItem("🥣", "辅食", RecordType.Complementary, "#FFF3E0"));
        Actions.Add(new QuickActionItem("🍶", "吸奶", RecordType.Pump, "#E8F5E9"));
        Actions.Add(new QuickActionItem("💊", "补给用药", RecordType.Supplement, "#F3E5F5"));
        Actions.Add(new QuickActionItem("🌡️", "体温", RecordType.Temperature, "#FFE8E8"));
        Actions.Add(new QuickActionItem("🌙", "睡眠", RecordType.Sleep, "#E8F0FE"));
        Actions.Add(new QuickActionItem("💩", "换尿布", RecordType.Diaper, "#FFF8E1"));
        Actions.Add(new QuickActionItem("🍼", "喂奶", RecordType.Feed, "#FFF0E6"));

        RebuildPages();
        Actions.CollectionChanged += (_, _) => RebuildPages();
    }

    /// <summary>
    /// 根据 Actions 重新分页。每页最多 PageSize 项。
    /// 项数 ≤8 时只 1 页（PipsPager 自动隐藏）；>8 时自动分页。
    /// </summary>
    private void RebuildPages()
    {
        Pages.Clear();
        for (int i = 0; i < Actions.Count; i += PageSize)
        {
            var page = Actions.Skip(i).Take(PageSize).ToList();
            Pages.Add(page);
        }
        if (CurrentPage >= Pages.Count) CurrentPage = 0;
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(HasMultiplePages));
    }

    partial void OnCurrentPageChanged(int value)
    {
        // 防御性：页码越界时回零
        if (value < 0 || value >= Pages.Count) CurrentPage = 0;
    }

    [RelayCommand]
    private void ToggleMenu()
    {
        if (IsRecordSheetOpen) return;
        IsMenuOpen = !IsMenuOpen;
    }

    [RelayCommand]
    private void CloseMenu() => IsMenuOpen = false;

    [RelayCommand]
    private void Select(string type)
    {
        IsMenuOpen = false;
        OpenRecordRequested?.Invoke(type);
    }
}
