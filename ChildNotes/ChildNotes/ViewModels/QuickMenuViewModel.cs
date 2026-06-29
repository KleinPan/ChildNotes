using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Models;

namespace ChildNotes.ViewModels;

/// <summary>
/// 右下角悬浮 + 按钮的 PopupBox 菜单状态机。
/// 菜单项从 + 按钮正上方垂直堆叠弹出。
/// 表单展示复用 RecordSheetViewModel（底部抽屉），由 MainShellViewModel.OpenQuickRecord 统一打开。
/// </summary>
public partial class QuickMenuViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isMenuOpen;

    /// <summary>
    /// 底部记录抽屉是否打开（由 MainShellViewModel 同步镜像）。
    /// 抽屉打开期间 FAB 必须隐藏，避免遮挡表单 / 重复触发新记录。
    /// </summary>
    [ObservableProperty] private bool _isRecordSheetOpen;

    /// <summary>
    /// FAB 是否在当前 tab 可见（仅首页可见，由 MainShellViewModel.SwitchTab 同步）。
    /// 与 IsRecordSheetOpen/IsMenuOpen 共同决定 FAB 最终可见性。
    /// </summary>
    [ObservableProperty] private bool _isFabEnabled = true;

    /// <summary>请求主壳层打开记录表单（由 MainShellViewModel 订阅）。</summary>
    public event Action<string>? OpenRecordRequested;

    /// <summary>PopupBox 菜单的快捷项（从 + 按钮上方垂直堆叠弹出）</summary>
    public ObservableCollection<QuickActionItem> Actions { get; } = new();

    public QuickMenuViewModel()
    {
        // 11 个快捷项：不常用的在上边（远离 + 按钮），常用的在下边（靠近 + 按钮）
        // 异常/生病记录为低频但重要功能，置于顶部（对齐小程序首页常驻红色入口的语义）
        Actions.Add(new QuickActionItem("🚨", "异常", RecordType.Abnormal, "#FFEBEE"));
        Actions.Add(new QuickActionItem("📏", "成长", RecordType.Growth, "#E0F2F1"));
        Actions.Add(new QuickActionItem("🍽️", "妈妈饮食", RecordType.MaternalFood, "#F2F7E8"));
        Actions.Add(new QuickActionItem("🥣", "辅食", RecordType.Complementary, "#FFF3E0"));
        Actions.Add(new QuickActionItem("🍶", "吸奶", RecordType.Pump, "#E8F5E9"));
        Actions.Add(new QuickActionItem("💊", "补给用药", RecordType.Supplement, "#F3E5F5"));
        Actions.Add(new QuickActionItem("🌡️", "体温", RecordType.Temperature, "#FFE8E8"));
        Actions.Add(new QuickActionItem("🌙", "睡眠", RecordType.Sleep, "#E8F0FE"));
        Actions.Add(new QuickActionItem("💩", "换尿布", RecordType.Diaper, "#FFF8E1"));
        Actions.Add(new QuickActionItem("🍼", "喂奶", RecordType.Feed, "#FFF0E6"));
        // Ai 记作为高频功能，紧贴喂奶下方（+ 按钮正上方第二位）
        Actions.Add(new QuickActionItem("🤖", "Ai记", RecordType.AiNote, "#EDE7F6"));
    }

    /// <summary>
    /// FAB 实际可见性：当前 tab 允许显示 且 未打开底部抽屉 且 菜单已关闭。
    /// 三个条件任一不满足即隐藏 FAB。派生属性，由 ObservableProperty 自动通知。
    /// </summary>
    public bool IsFabVisible => IsFabEnabled && !IsRecordSheetOpen && !IsMenuOpen;

    partial void OnIsMenuOpenChanged(bool value) => OnPropertyChanged(nameof(IsFabVisible));
    partial void OnIsRecordSheetOpenChanged(bool value) => OnPropertyChanged(nameof(IsFabVisible));
    partial void OnIsFabEnabledChanged(bool value) => OnPropertyChanged(nameof(IsFabVisible));

    [RelayCommand]
    private void ToggleMenu()
    {
        // 抽屉打开期间禁止切换菜单（理论上 FAB 已隐藏，这里做防御性拦截）
        if (IsRecordSheetOpen) return;
        IsMenuOpen = !IsMenuOpen;
    }

    [RelayCommand]
    private void CloseMenu() => IsMenuOpen = false;

    [RelayCommand]
    private void Select(string type)
    {
        IsMenuOpen = false;
        // 委托给 MainShellViewModel.OpenQuickRecord 打开底部抽屉 RecordSheetView
        OpenRecordRequested?.Invoke(type);
    }
}
