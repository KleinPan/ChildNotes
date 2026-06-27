using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Models;

namespace ChildNotes.ViewModels;

/// <summary>
/// 右下角悬浮 + 按钮的 PopupBox 菜单 + 居中卡片表单的状态机。
/// 菜单项从 + 按钮正上方垂直堆叠弹出。复用 <see cref="RecordSheetViewModel"/> 的表单逻辑。
/// </summary>
public partial class QuickMenuViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isMenuOpen;
    [ObservableProperty] private bool _isCardOpen;
    [ObservableProperty] private string _cardTitle = string.Empty;

    /// <summary>复用主壳层的 RecordSheet 实例（表单状态 + 保存逻辑）</summary>
    public RecordSheetViewModel RecordSheet { get; }

    /// <summary>PopupBox 菜单的 9 个快捷项（从 + 按钮上方垂直堆叠弹出）</summary>
    public ObservableCollection<QuickActionItem> Actions { get; } = new();

    public event Action? Saved;

    public QuickMenuViewModel(RecordSheetViewModel recordSheet)
    {
        RecordSheet = recordSheet;
        // 保存成功后关闭卡片并通知上层刷新
        recordSheet.Saved += () =>
        {
            IsCardOpen = false;
            Saved?.Invoke();
        };

        // 9 个快捷项：不常用的在上边（远离 + 按钮），常用的在下边（靠近 + 按钮）
        Actions.Add(new QuickActionItem("📏", "成长", RecordType.Growth, "#E0F2F1"));
        Actions.Add(new QuickActionItem("🍽️", "妈妈饮食", RecordType.MaternalFood, "#F2F7E8"));
        Actions.Add(new QuickActionItem("🥣", "辅食", RecordType.Complementary, "#FFF3E0"));
        Actions.Add(new QuickActionItem("🍶", "吸奶", RecordType.Pump, "#E8F5E9"));
        Actions.Add(new QuickActionItem("💊", "补给用药", RecordType.Supplement, "#F3E5F5"));
        Actions.Add(new QuickActionItem("🌡️", "体温", RecordType.Temperature, "#FFE8E8"));
        Actions.Add(new QuickActionItem("🌙", "睡眠", RecordType.Sleep, "#E8F0FE"));
        Actions.Add(new QuickActionItem("💩", "换尿布", RecordType.Diaper, "#FFF8E1"));
        Actions.Add(new QuickActionItem("🍼", "喂奶", RecordType.Feed, "#FFF0E6"));
    }

    [RelayCommand]
    private void ToggleMenu()
    {
        IsMenuOpen = !IsMenuOpen;
        if (!IsMenuOpen) return;
        // 打开菜单时确保卡片已关闭
        IsCardOpen = false;
    }

    [RelayCommand]
    private void CloseMenu() => IsMenuOpen = false;

    [RelayCommand]
    private void Select(string type)
    {
        IsMenuOpen = false;
        RecordSheet.Open(type);
        CardTitle = RecordSheet.Title;
        IsCardOpen = true;
    }

    [RelayCommand]
    private void CloseCard()
    {
        IsCardOpen = false;
        RecordSheet.CloseCommand.Execute(null);
    }
}
