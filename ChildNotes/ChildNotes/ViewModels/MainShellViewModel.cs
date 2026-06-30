using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Shared.Constants;

namespace ChildNotes.ViewModels;

public partial class MainShellViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase _currentTab;
    [ObservableProperty] private bool _isHomeSelected = true;
    [ObservableProperty] private bool _isFeedingSelected;
    [ObservableProperty] private bool _isGrowthSelected;
    [ObservableProperty] private bool _isMineSelected;

    [ObservableProperty] private bool _isRecordSheetOpen;
    [ObservableProperty] private RecordSheetViewModel _recordSheet;
    [ObservableProperty] private QuickMenuViewModel _quickMenu;

    /// <summary>
    /// 底部抽屉打开/关闭时同步到 QuickMenu，触发 FAB 显隐派生属性变更。
    /// </summary>
    partial void OnIsRecordSheetOpenChanged(bool value)
    {
        if (QuickMenu is not null)
        {
            QuickMenu.IsRecordSheetOpen = value;
            DevLogger.Log("Shell", $"OnIsRecordSheetOpenChanged: {value} -> FAB will be {(value ? "hidden" : "shown")}");
        }
    }

    /// <summary>
    /// Ai 记弹层打开/关闭时同步到 QuickMenu，触发 FAB 显隐派生属性变更。
    /// 与 OnIsRecordSheetOpenChanged 对称，确保 Ai 记弹出时 + 按钮正确隐藏。
    /// </summary>
    partial void OnIsAiNoteOpenChanged(bool value)
    {
        if (QuickMenu is not null)
        {
            QuickMenu.IsAiNoteOpen = value;
            DevLogger.Log("Shell", $"OnIsAiNoteOpenChanged: {value} -> FAB will be {(value ? "hidden" : "shown")}");
        }
    }
    [ObservableProperty] private bool _isBabySetupOpen;
    [ObservableProperty] private BabySetupViewModel _babySetup;
    [ObservableProperty] private bool _isBabyManagerOpen;
    [ObservableProperty] private BabyManagerViewModel _babyManager;
    [ObservableProperty] private bool _isStatisticsOpen;
    [ObservableProperty] private StatisticsViewModel _statistics;
    [ObservableProperty] private bool _isPointsOpen;
    [ObservableProperty] private PointsViewModel _points;
    [ObservableProperty] private bool _isAiAnalysisOpen;
    [ObservableProperty] private AiAnalysisViewModel _aiAnalysis;
    [ObservableProperty] private bool _isAiSettingsOpen;
    [ObservableProperty] private AiSettingsViewModel _aiSettings;
    [ObservableProperty] private bool _isSyncSettingsOpen;
    [ObservableProperty] private SyncSettingsViewModel _syncSettings;
    [ObservableProperty] private bool _isFamilyOpen;
    [ObservableProperty] private FamilyViewModel _family;
    [ObservableProperty] private bool _isAiNoteOpen;
    [ObservableProperty] private AiNoteViewModel _aiNote;

    public HomeViewModel Home { get; }
    public FeedingViewModel Feeding { get; }
    public GrowthViewModel Growth { get; }
    public MineViewModel Mine { get; }

    public event Action? LogoutRequested;

    /// <summary>
    /// 弹层注册表：每个弹层项记录 VM、打开/关闭动作与 IsOpen 探测器。
    /// 关闭顺序由枚举顺序固定（最近打开的先关），避免在 SwitchTab/OnLogout 中重复罗列 IsXxxOpen=false。
    /// </summary>
    private readonly List<OverlayEntry> _overlays = new();

    /// <summary>记录表单与快捷菜单：非 OverlayKind 枚举内成员，单独关闭。</summary>
    private void CloseRecordSheetAndQuickMenu()
    {
        IsRecordSheetOpen = false;
        if (QuickMenu.IsMenuOpen) QuickMenu.CloseMenuCommand.Execute(null);
    }

    private sealed class OverlayEntry
    {
        public ViewModelBase Vm { get; }
        public Action Open { get; }
        public Action Close { get; }
        public Func<bool> IsOpen { get; }
        public OverlayEntry(ViewModelBase vm, Action open, Action close, Func<bool> isOpen)
        { Vm = vm; Open = open; Close = close; IsOpen = isOpen; }
    }

    /// <summary>
    /// 注册一个弹层：
    /// - 自动订阅其 BackRequested 以触发关闭
    /// - open 动作可选（部分弹层需要预加载）
    /// </summary>
    private void RegisterOverlay(ViewModelBase vm, Action close, Func<bool> isOpen, Action? open = null)
    {
        vm.BackRequested += () => close();
        _overlays.Add(new OverlayEntry(vm, open ?? (() => { }), close, isOpen));
    }

    /// <summary>关闭全部已注册弹层（不含记录表单与快捷菜单，那两项请显式调用 CloseRecordSheetAndQuickMenu）。</summary>
    private void CloseAllOverlays()
    {
        foreach (var entry in _overlays) entry.Close();
    }

    /// <summary>系统返回键处理：关闭最近一个处于打开状态的弹层；若都没有打开，回落到关闭记录表单/快捷菜单。</summary>
    public bool HandleSystemBack()
    {
        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            if (_overlays[i].IsOpen())
            {
                _overlays[i].Close();
                return true;
            }
        }
        if (IsRecordSheetOpen || QuickMenu.IsMenuOpen)
        {
            CloseRecordSheetAndQuickMenu();
            return true;
        }
        return false;
    }

    public MainShellViewModel()
    {
        Home = new HomeViewModel();
        Feeding = new FeedingViewModel();
        Growth = new GrowthViewModel();
        Mine = new MineViewModel();
        _currentTab = Home;

        Home.StatisticsRequested += OpenStatistics;
        Home.CheckInRequested += OpenPoints;
        Home.QuickRecordRequested += OpenQuickRecord;
        Feeding.EditRequested += OpenEditRecord;
        Mine.LogoutRequested += OnLogout;

        _recordSheet = new RecordSheetViewModel();
        _recordSheet.Saved += OnRecordSaved;

        _quickMenu = new QuickMenuViewModel();
        _quickMenu.OpenRecordRequested += OpenQuickRecord;

        _babySetup = new BabySetupViewModel();
        _babySetup.Completed += OnBabySetupCompleted;

        _babyManager = new BabyManagerViewModel();
        _babyManager.BabyChanged += OnBabyChanged;

        _statistics = new StatisticsViewModel();

        _points = new PointsViewModel();

        _aiAnalysis = new AiAnalysisViewModel();
        _aiAnalysis.ConfigRequired += OpenAiSettings;

        _aiSettings = new AiSettingsViewModel();

        _syncSettings = new SyncSettingsViewModel();

        _family = new FamilyViewModel();

        _aiNote = new AiNoteViewModel();
        _aiNote.Saved += OnRecordSaved;

        // 注册弹层（顺序决定系统返回键的关闭优先级：后注册的先关）
        RegisterOverlay(BabySetup, () => IsBabySetupOpen = false, () => IsBabySetupOpen);
        RegisterOverlay(BabyManager, () => IsBabyManagerOpen = false, () => IsBabyManagerOpen);
        RegisterOverlay(Statistics, () => IsStatisticsOpen = false, () => IsStatisticsOpen);
        RegisterOverlay(Points, () => IsPointsOpen = false, () => IsPointsOpen);
        RegisterOverlay(AiAnalysis, () => IsAiAnalysisOpen = false, () => IsAiAnalysisOpen);
        RegisterOverlay(AiSettings, () => IsAiSettingsOpen = false, () => IsAiSettingsOpen);
        RegisterOverlay(SyncSettings, () => IsSyncSettingsOpen = false, () => IsSyncSettingsOpen);
        RegisterOverlay(Family, () => IsFamilyOpen = false, () => IsFamilyOpen);
        RegisterOverlay(AiNote, () => IsAiNoteOpen = false, () => IsAiNoteOpen, OpenAiNote);
    }

    /// <summary>打开 AI 智能记模态窗口。</summary>
    public void OpenAiNote()
    {
        if (ServiceProvider.Instance.AppState.CurrentBaby is null)
        {
            OpenBabySetup();
            return;
        }
        AiNote.Activate();
        IsAiNoteOpen = true;
    }

    [RelayCommand]
    private void SwitchTab(string tab)
    {
        CloseRecordSheetAndQuickMenu();
        QuickMenu.IsFabEnabled = tab == "home";
        CloseAllOverlays();

        IsHomeSelected = tab == "home";
        IsFeedingSelected = tab == "feeding";
        IsGrowthSelected = tab == "growth";
        IsMineSelected = tab == "mine";

        CurrentTab = tab switch
        {
            "home" => Home,
            "feeding" => Feeding,
            "growth" => Growth,
            "mine" => Mine,
            _ => Home,
        };
        if (CurrentTab is IActivatable activatable) activatable.Activate();
    }

    public void ActivateHome()
    {
        CurrentTab = Home;
        Home.Activate();
    }

    public async void OpenQuickRecord(string recordType)
    {
        DevLogger.Log("Shell", $"OpenQuickRecord type={recordType}");
        if (ServiceProvider.Instance.AppState.CurrentBaby is null)
        {
            DevLogger.Log("Shell", "OpenQuickRecord: no current baby, open BabySetup");
            OpenBabySetup();
            return;
        }
        // Ai 记走单独的模态窗口，不走底部抽屉
        if (recordType == RecordType.AiNote)
        {
            OpenAiNote();
            return;
        }
        // 先设置抽屉可见（占位立即响应），再异步打开（疫苗类型会异步加载数据）
        IsRecordSheetOpen = true;
        await RecordSheet.OpenAsync(recordType);
        DevLogger.Log("Shell", $"OpenQuickRecord done: IsRecordSheetOpen={IsRecordSheetOpen}, SheetTitle={RecordSheet.SheetTitle}");
    }

    /// <summary>
    /// 编辑现有记录：复用 RecordSheet 的编辑模式（同一套表单 XAML，所有字段可编辑）。
    /// 由 FeedingViewModel.EditRecord 等调用。
    /// </summary>
    public void OpenEditRecord(ChildRecord record)
    {
        DevLogger.Log("Shell", $"OpenEditRecord type={record.RecordType}, id={record.Id}");
        RecordSheet.Edit(record);
        IsRecordSheetOpen = true;
        DevLogger.Log("Shell", $"OpenEditRecord done: IsRecordSheetOpen={IsRecordSheetOpen}, SheetTitle={RecordSheet.SheetTitle}");
    }

    public void OpenBabySetup()
    {
        BabySetup.Reset();
        IsBabySetupOpen = true;
    }

    public async void OpenBabyManager()
    {
        IsBabyManagerOpen = true;
        await BabyManager.LoadAsync();
    }

    public async void OpenStatistics()
    {
        IsStatisticsOpen = true;
        await Statistics.LoadAsync();
    }

    public async void OpenPoints()
    {
        IsPointsOpen = true;
        await Points.LoadAsync();
    }

    public async void OpenAiAnalysis()
    {
        IsAiAnalysisOpen = true;
        await AiAnalysis.LoadAsync();
    }

    public void OpenAiSettings()
    {
        AiSettings.Activate();
        IsAiSettingsOpen = true;
    }

    public void OpenSyncSettings()
    {
        IsSyncSettingsOpen = true;
    }

    public async void OpenFamily()
    {
        IsFamilyOpen = true;
        await Family.LoadAsync();
    }

    /// <summary>OnRecordSaved 防抖取消令牌：5 秒内多次保存只触发一次刷新链。</summary>
    private CancellationTokenSource? _savedRefreshCts;

    private async void OnRecordSaved()
    {
        DevLogger.Log("Shell", "OnRecordSaved: closing sheet, debouncing Home/Feeding refresh");
        IsRecordSheetOpen = false;

        // 防抖 100ms：快速连续保存（如批量补记）只触发一次刷新
        _savedRefreshCts?.Cancel();
        _savedRefreshCts?.Dispose();
        _savedRefreshCts = new CancellationTokenSource();
        var ct = _savedRefreshCts.Token;
        try
        {
            await Task.Delay(100, ct);
            await Home.RefreshAsync();
            if (CurrentTab is FeedingViewModel feeding) feeding.Activate();
            // Statistics 不再主动刷新：用户进入统计页时 OpenStatistics 会触发 LoadAsync
            // 避免保存记录后无谓地刷新用户未查看的页面
        }
        catch (OperationCanceledException)
        {
            DevLogger.Log("Shell", "OnRecordSaved: refresh debounced (superseded by newer save)");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Shell", "OnRecordSaved refresh failed: " + ex.Message);
        }
    }

    private async void OnBabySetupCompleted()
    {
        IsBabySetupOpen = false;
        await Home.RefreshAsync();
    }

    private async void OnBabyChanged()
    {
        await Home.RefreshAsync();
        if (CurrentTab is FeedingViewModel feeding) feeding.Activate();
    }

    private void OnLogout()
    {
        CloseRecordSheetAndQuickMenu();
        CloseAllOverlays();
        LogoutRequested?.Invoke();
    }

    public void ActivateHomeAfterLogin()
    {
        IsHomeSelected = true;
        IsFeedingSelected = false;
        IsGrowthSelected = false;
        IsMineSelected = false;
        CurrentTab = Home;
        QuickMenu.IsFabEnabled = true;
        Home.Activate();
    }
}

public interface IActivatable
{
    void Activate();
}
