using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;

namespace ChildNotes.ViewModels;

public partial class MainShellViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase _currentTab;
    [ObservableProperty] private bool _isHomeSelected = true;
    [ObservableProperty] private bool _isFeedingSelected;
    [ObservableProperty] private bool _isGrowthSelected;
    [ObservableProperty] private bool _isMineSelected;

    /// <summary>
    /// 首页输入栏和功能面板是否可见：首页 Tab 且 未打开底部抽屉。
    /// 抽屉打开时隐藏，避免与 RecordSheet 底部抽屉视觉重叠。
    /// </summary>
    public bool IsQuickInputVisible => IsHomeSelected && !IsRecordSheetOpen;

    partial void OnIsHomeSelectedChanged(bool value) => OnPropertyChanged(nameof(IsQuickInputVisible));
    partial void OnIsRecordSheetOpenChanged(bool value)
    {
        if (QuickMenu is not null)
        {
            QuickMenu.IsRecordSheetOpen = value;
        }
        OnPropertyChanged(nameof(IsQuickInputVisible));
    }

    [ObservableProperty] private bool _isRecordSheetOpen;
    [ObservableProperty] private RecordSheetViewModel _recordSheet;
    [ObservableProperty] private QuickMenuViewModel _quickMenu;

    /// <summary>
    /// 首页底部快捷输入栏：承担原 Ai 记模态的全部输入/解析/保存职责。
    /// 仅在首页 Tab 显示，输入文本即触发解析（点发送按钮）。
    /// </summary>
    [ObservableProperty] private QuickInputViewModel _quickInput;

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

    [ObservableProperty] private bool _isDeveloperOptionsOpen;
    [ObservableProperty] private DeveloperOptionsViewModel _developerOptions;

    /// <summary>"程序日志"弹层（从开发者选项打开）。</summary>
    [ObservableProperty] private bool _isAppLogOpen;
    [ObservableProperty] private AppLogViewModel _appLog;

    /// <summary>"使用帮助"弹层（从"我的"页打开）。</summary>
    [ObservableProperty] private bool _isHelpOpen;
    [ObservableProperty] private HelpViewModel _help;

    /// <summary>隐私政策弹层（从"我的"页打开查看完整协议）。</summary>
    [ObservableProperty] private bool _isPrivacyPolicyOpen;
    [ObservableProperty] private PrivacyConsentViewModel _privacyPolicy;

    /// <summary>应用内消息中心弹层。</summary>
    [ObservableProperty] private bool _isInAppMessageOpen;
    [ObservableProperty] private InAppMessageViewModel _inAppMessage;

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

    /// <summary>
    /// 系统返回键处理，按优先级依次：
    /// 1. 关闭最近一个处于打开状态的弹层；
    /// 2. 关闭记录表单/快捷菜单；
    /// 3. 非首页 Tab 回到首页（符合 Android 返回导航规范，避免直接退出应用）；
    /// 4. 以上都不满足 → 返回 false，由系统执行默认行为（退出应用）。
    /// </summary>
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
        if (!IsHomeSelected)
        {
            SwitchTabCommand.Execute("home");
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
        _recordSheet.Closed += OnRecordSheetClosed;
        _recordSheet.VaccineInlineChanged += OnVaccineInlineChanged;

        _quickMenu = new QuickMenuViewModel();
        _quickMenu.OpenRecordRequested += OpenQuickRecord;

        _quickInput = new QuickInputViewModel();
        _quickInput.Saved += OnRecordSaved;
        // + 按钮点击 → 转发到功能面板展开/收起
        _quickInput.ToggleActionsRequested += () => QuickMenu.ToggleMenuCommand.Execute(null);
        // 输入内容时强制收起功能面板
        _quickInput.CloseActionsRequested += () => QuickMenu.CloseMenuCommand.Execute(null);

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

        _developerOptions = new DeveloperOptionsViewModel();
        _developerOptions.OpenAppLogRequested += OpenAppLog;

        _appLog = new AppLogViewModel();

        _help = new HelpViewModel();

        // 隐私政策弹层：只读模式，仅展示完整协议 + 关闭按钮
        _privacyPolicy = new PrivacyConsentViewModel { IsReadOnly = true };
        _privacyPolicy.ConsentGiven += () => IsPrivacyPolicyOpen = false;

        // 应用内消息中心
        _inAppMessage = new InAppMessageViewModel();
        // 消息中心内标记已读/全部已读后，同步刷新"我的"页红点
        _inAppMessage.UnreadCountChanged += () => Mine.RefreshUnreadMessages();

        // 注册弹层（顺序决定系统返回键的关闭优先级：后注册的先关）
        RegisterOverlay(BabySetup, () => IsBabySetupOpen = false, () => IsBabySetupOpen);
        RegisterOverlay(BabyManager, () => IsBabyManagerOpen = false, () => IsBabyManagerOpen);
        RegisterOverlay(Statistics, () => IsStatisticsOpen = false, () => IsStatisticsOpen);
        RegisterOverlay(Points, () => IsPointsOpen = false, () => IsPointsOpen);
        RegisterOverlay(AiAnalysis, () => IsAiAnalysisOpen = false, () => IsAiAnalysisOpen);
        RegisterOverlay(AiSettings, () => IsAiSettingsOpen = false, () => IsAiSettingsOpen);
        RegisterOverlay(SyncSettings, () => IsSyncSettingsOpen = false, () => IsSyncSettingsOpen);
        RegisterOverlay(Family, () => IsFamilyOpen = false, () => IsFamilyOpen);
        RegisterOverlay(DeveloperOptions, () => IsDeveloperOptionsOpen = false, () => IsDeveloperOptionsOpen);
        RegisterOverlay(AppLog, () => IsAppLogOpen = false, () => IsAppLogOpen);
        RegisterOverlay(Help, () => IsHelpOpen = false, () => IsHelpOpen);
        RegisterOverlay(PrivacyPolicy, () => IsPrivacyPolicyOpen = false, () => IsPrivacyPolicyOpen);
        RegisterOverlay(InAppMessage, () => IsInAppMessageOpen = false, () => IsInAppMessageOpen);
    }

    [RelayCommand]
    private void SwitchTab(string tab)
    {
        CloseRecordSheetAndQuickMenu();
        // 切 Tab 时关闭功能面板（输入栏在非首页隐藏，面板也应关闭）
        if (QuickMenu.IsMenuOpen) QuickMenu.CloseMenuCommand.Execute(null);
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

    /// <summary>
    /// 获取当前活动 tab 的字符串标识（home/feeding/growth/mine）。
    /// 供 MainActivity.OnSaveInstanceState 保存 UI 状态，Activity 重建后还原。
    /// </summary>
    public string GetCurrentTabId()
    {
        if (ReferenceEquals(CurrentTab, Feeding)) return "feeding";
        if (ReferenceEquals(CurrentTab, Growth)) return "growth";
        if (ReferenceEquals(CurrentTab, Mine)) return "mine";
        return "home";
    }

    /// <summary>
    /// 供 MainActivity.OnRestoreInstanceState 调用，Activity 重建后还原 tab。
    /// 不走 SwitchTabCommand 是为了避免触发 CloseAllOverlays（恢复期无弹层可关）。
    /// </summary>
    public void RestoreTab(string tabId)
    {
        var tab = tabId is "feeding" or "growth" or "mine" ? tabId : "home";
        // 切 Tab 时关闭功能面板（输入栏在非首页隐藏，面板也应关闭）
        if (QuickMenu.IsMenuOpen) QuickMenu.CloseMenuCommand.Execute(null);
        IsHomeSelected = tab == "home";
        IsFeedingSelected = tab == "feeding";
        IsGrowthSelected = tab == "growth";
        IsMineSelected = tab == "mine";
        CurrentTab = tab switch
        {
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
        try
        {
            DevLogger.Log("Shell", $"OpenQuickRecord type={recordType}");
            if (ServiceProvider.Instance.AppState.CurrentBaby is null)
            {
                DevLogger.Log("Shell", "OpenQuickRecord: no current baby, open BabySetup");
                OpenBabySetup();
                return;
            }
            // 先设置抽屉可见（占位立即响应），再异步打开（疫苗类型会异步加载数据）
            IsRecordSheetOpen = true;
            await RecordSheet.OpenAsync(recordType);
            DevLogger.Log("Shell", $"OpenQuickRecord done: IsRecordSheetOpen={IsRecordSheetOpen}, SheetTitle={RecordSheet.SheetTitle}");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Shell", "OpenQuickRecord failed: " + ex);
            IsRecordSheetOpen = false;
        }
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
        try
        {
            IsBabyManagerOpen = true;
            await BabyManager.LoadAsync();
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OpenBabyManager failed: " + ex); }
    }

    public async void OpenStatistics()
    {
        try
        {
            IsStatisticsOpen = true;
            await Statistics.LoadAsync();
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OpenStatistics failed: " + ex); }
    }

    public async void OpenPoints()
    {
        try
        {
            IsPointsOpen = true;
            await Points.LoadAsync();
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OpenPoints failed: " + ex); }
    }

    public async void OpenAiAnalysis()
    {
        try
        {
            IsAiAnalysisOpen = true;
            await AiAnalysis.LoadAsync();
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OpenAiAnalysis failed: " + ex); }
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
        try
        {
            IsFamilyOpen = true;
            await Family.LoadAsync();
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OpenFamily failed: " + ex); }
    }

    public void OpenDeveloperOptions()
    {
        IsDeveloperOptionsOpen = true;
    }

    /// <summary>打开"程序日志"页（开发者选项内入口）。</summary>
    public void OpenAppLog()
    {
        IsAppLogOpen = true;
        AppLog.Activate();
    }

    /// <summary>打开"使用帮助"页。</summary>
    public void OpenHelp()
    {
        IsHelpOpen = true;
    }

    /// <summary>打开隐私政策查看（只读模式，不展示同意/不同意按钮）。</summary>
    public void OpenPrivacyPolicy()
    {
        // 直接展示完整协议视图
        PrivacyPolicy.ViewFullPolicyCommand.Execute(null);
        IsPrivacyPolicyOpen = true;
    }

    /// <summary>打开用户协议查看（只读模式，不展示同意/不同意按钮）。</summary>
    public void OpenUserAgreement()
    {
        // 直接展示完整协议视图，并切到用户协议 Tab
        PrivacyPolicy.ViewFullAgreementCommand.Execute(null);
        IsPrivacyPolicyOpen = true;
    }

    /// <summary>打开应用内消息中心。</summary>
    public async void OpenInAppMessage()
    {
        try
        {
            IsInAppMessageOpen = true;
            await InAppMessage.LoadAsync();
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OpenInAppMessage failed: " + ex); }
    }

    /// <summary>OnRecordSaved 防抖取消令牌：100ms 内多次保存只触发一次刷新链。</summary>
    private CancellationTokenSource? _savedRefreshCts;

    /// <summary>
    /// 记录抽屉关闭时统一重置 IsRecordSheetOpen（保存和 X 关闭共用）。
    /// 这是修复"关闭弹窗后 FAB 不恢复"bug 的核心：X 按钮关闭路径此前未重置该标志。
    /// </summary>
    private void OnRecordSheetClosed()
    {
        DevLogger.Log("Shell", "OnRecordSheetClosed: setting IsRecordSheetOpen=false -> FAB should reappear");
        IsRecordSheetOpen = false;
    }

    /// <summary>疫苗内联操作（已打/跳过/取消）后：清除疫苗时间轴缓存并刷新首页数据，不关闭抽屉。
    /// 必须清缓存，否则下次打开补记面板 LoadAsync 会命中 s_preloadedGroups 返回旧状态。</summary>
    private async void OnVaccineInlineChanged()
    {
        // 清除疫苗时间轴缓存（BuildPlans + 预加载），确保下次打开补记面板从 DB 重建
        ChildNotes.Services.VaccineTimelineBuilder.InvalidateCache();
        ChildNotes.ViewModels.VaccineFormViewModel.InvalidatePreload();

        _savedRefreshCts?.Cancel();
        _savedRefreshCts?.Dispose();
        _savedRefreshCts = new CancellationTokenSource();
        var ct = _savedRefreshCts.Token;
        try
        {
            await Task.Delay(100, ct);
            await Home.RefreshAsync();
        }
        catch (OperationCanceledException) { }
    }

    private async void OnRecordSaved()
    {
        DevLogger.Log("Shell", "OnRecordSaved: closing sheet, debouncing Home/Feeding refresh");
        IsRecordSheetOpen = false;

        // 清除疫苗时间轴缓存（BuildPlans + 预加载），确保下次打开补记面板时重建最新数据
        ChildNotes.Services.VaccineTimelineBuilder.InvalidateCache();
        ChildNotes.ViewModels.VaccineFormViewModel.InvalidatePreload();

        // 防抖 100ms：快速连续保存（如批量补记）只触发一次刷新
        _savedRefreshCts?.Cancel();
        _savedRefreshCts?.Dispose();
        _savedRefreshCts = new CancellationTokenSource();
        var ct = _savedRefreshCts.Token;
        try
        {
            await Task.Delay(100, ct);
            await Home.RefreshAsync();
            // 无条件刷新 Feeding 列表：用户可能在首页用 AI 输入栏生成记录后切到喂奶 Tab 查看，
            // 也可能就在喂奶 Tab 内通过 RecordSheet 添加。两种场景都需要刷新。
            Feeding.Activate();
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
        try
        {
            IsBabySetupOpen = false;
            await Home.RefreshAsync();
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OnBabySetupCompleted failed: " + ex); }
    }

    private async void OnBabyChanged()
    {
        try
        {
            await Home.RefreshAsync();
            if (CurrentTab is FeedingViewModel feeding) feeding.Activate();
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OnBabyChanged failed: " + ex); }
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
        Home.Activate();
    }
}

public interface IActivatable
{
    void Activate();
}
