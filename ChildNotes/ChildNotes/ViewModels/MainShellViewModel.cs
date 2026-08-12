using System.Linq;
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

    partial void OnIsHomeSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsQuickInputVisible));
        RaiseInterceptBackChanged();
    }
    partial void OnIsRecordSheetOpenChanged(bool value)
    {
        if (QuickMenu is not null)
        {
            QuickMenu.IsRecordSheetOpen = value;
        }
        OnPropertyChanged(nameof(IsQuickInputVisible));
        RaiseInterceptBackChanged();
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
    private BabySetupViewModel? _babySetup;
    [ObservableProperty] private bool _isBabyManagerOpen;
    private BabyManagerViewModel? _babyManager;
    [ObservableProperty] private bool _isStatisticsOpen;
    private StatisticsViewModel? _statistics;
    [ObservableProperty] private bool _isPointsOpen;
    private PointsViewModel? _points;
    [ObservableProperty] private bool _isMembershipOpen;
    private MembershipViewModel? _membership;
    [ObservableProperty] private bool _isAiAnalysisOpen;
    private AiAnalysisViewModel? _aiAnalysis;
    [ObservableProperty] private bool _isAiSettingsOpen;
    private AiSettingsViewModel? _aiSettings;
    [ObservableProperty] private bool _isReminderSettingsOpen;
    private ReminderSettingsViewModel? _reminderSettings;
    [ObservableProperty] private bool _isSyncSettingsOpen;
    private SyncSettingsViewModel? _syncSettings;
    [ObservableProperty] private bool _isFamilyOpen;
    private FamilyViewModel? _family;

    [ObservableProperty] private bool _isDeveloperOptionsOpen;
    private DeveloperOptionsViewModel? _developerOptions;

    /// <summary>"使用帮助"弹层（从"我的"页打开）。</summary>
    [ObservableProperty] private bool _isHelpOpen;
    private HelpViewModel? _help;

    /// <summary>隐私政策弹层（从"我的"页打开查看完整协议）。</summary>
    [ObservableProperty] private bool _isPrivacyPolicyOpen;
    private PrivacyConsentViewModel? _privacyPolicy;

    /// <summary>应用内消息中心弹层。</summary>
    [ObservableProperty] private bool _isInAppMessageOpen;
    private InAppMessageViewModel? _inAppMessage;

    /// <summary>语言设置弹层（中英文切换）。</summary>
    [ObservableProperty] private bool _isLanguageSettingsOpen;
    private LanguageSettingsViewModel? _languageSettings;

    // ===== 弹层 VM 懒加载访问器 =====
    // 首次访问时创建实例并注册到 _overlays（系统返回键关闭优先级表）。
    // 后续访问直接返回缓存实例。
    // 注：事件订阅在创建时完成，与原构造函数行为一致。
    public BabySetupViewModel BabySetup => _babySetup ??= CreateAndRegisterOverlay(
        () => new BabySetupViewModel(),
        vm => { vm.Completed += OnBabySetupCompleted; },
        () => IsBabySetupOpen = false, () => IsBabySetupOpen);
    public BabyManagerViewModel BabyManager => _babyManager ??= CreateAndRegisterOverlay(
        () => new BabyManagerViewModel(),
        vm => { vm.BabyChanged += OnBabyChanged; },
        () => IsBabyManagerOpen = false, () => IsBabyManagerOpen);
    public StatisticsViewModel Statistics => _statistics ??= CreateAndRegisterOverlay(
        () => new StatisticsViewModel(),
        _ => { },
        () => IsStatisticsOpen = false, () => IsStatisticsOpen);
    public PointsViewModel Points => _points ??= CreateAndRegisterOverlay(
        () => new PointsViewModel(),
        _ => { },
        () => IsPointsOpen = false, () => IsPointsOpen);
    public MembershipViewModel Membership => _membership ??= CreateAndRegisterOverlay(
        () => new MembershipViewModel(),
        vm => { vm.PaymentSucceeded += OnMembershipPaymentSucceeded; },
        () => IsMembershipOpen = false, () => IsMembershipOpen);
    public AiAnalysisViewModel AiAnalysis => _aiAnalysis ??= CreateAndRegisterOverlay(
        () => new AiAnalysisViewModel(),
        vm =>
        {
            vm.ConfigRequired += OpenAiSettings;
            vm.PointsRequired += OpenPoints;
            vm.MembershipRequired += OpenMembership;
        },
        () => IsAiAnalysisOpen = false, () => IsAiAnalysisOpen);
    public AiSettingsViewModel AiSettings => _aiSettings ??= CreateAndRegisterOverlay(
        () => new AiSettingsViewModel(),
        _ => { },
        () => IsAiSettingsOpen = false, () => IsAiSettingsOpen);
    public ReminderSettingsViewModel ReminderSettings => _reminderSettings ??= CreateAndRegisterOverlay(
        () => new ReminderSettingsViewModel(),
        _ => { },
        () => IsReminderSettingsOpen = false, () => IsReminderSettingsOpen);
    public SyncSettingsViewModel SyncSettings => _syncSettings ??= CreateAndRegisterOverlay(
        () => new SyncSettingsViewModel(),
        _ => { },
        () => IsSyncSettingsOpen = false, () => IsSyncSettingsOpen);
    public FamilyViewModel Family => _family ??= CreateAndRegisterOverlay(
        () => new FamilyViewModel(),
        _ => { },
        () => IsFamilyOpen = false, () => IsFamilyOpen);
    public DeveloperOptionsViewModel DeveloperOptions => _developerOptions ??= CreateAndRegisterOverlay(
        () => new DeveloperOptionsViewModel(),
        _ => { },
        () => IsDeveloperOptionsOpen = false, () => IsDeveloperOptionsOpen);
    public HelpViewModel Help => _help ??= CreateAndRegisterOverlay(
        () => new HelpViewModel(),
        _ => { },
        () => IsHelpOpen = false, () => IsHelpOpen);
    public PrivacyConsentViewModel PrivacyPolicy => _privacyPolicy ??= CreateAndRegisterOverlay(
        () =>
        {
            // 隐私政策弹层：只读模式，仅展示完整协议 + 关闭按钮
            var vm = new PrivacyConsentViewModel { IsReadOnly = true };
            vm.ConsentGiven += () => IsPrivacyPolicyOpen = false;
            return vm;
        },
        _ => { },
        () => IsPrivacyPolicyOpen = false, () => IsPrivacyPolicyOpen);
    public InAppMessageViewModel InAppMessage => _inAppMessage ??= CreateAndRegisterOverlay(
        () => new InAppMessageViewModel(),
        vm => { vm.UnreadCountChanged += () => _ = Mine.RefreshUnreadMessagesAsync(); },
        () => IsInAppMessageOpen = false, () => IsInAppMessageOpen);
    public LanguageSettingsViewModel LanguageSettings => _languageSettings ??= CreateAndRegisterOverlay(
        () => new LanguageSettingsViewModel(),
        _ => { },
        () => IsLanguageSettingsOpen = false, () => IsLanguageSettingsOpen);

    /// <summary>
    /// 创建弹层 VM 并注册到 _overlays（返回键关闭优先级表）。
    /// 泛型参数避免 lambda 返回 object 导致重复拆装箱。
    /// </summary>
    private T CreateAndRegisterOverlay<T>(
        Func<T> factory,
        Action<T> wireEvents,
        Action close,
        Func<bool> isOpen) where T : ViewModelBase
    {
        var vm = factory();
        wireEvents(vm);
        RegisterOverlay(vm, close, isOpen);
        return vm;
    }

    // ===== Tab VM 懒加载 =====
    // Home 立即创建：构造完成后立即 ActivateHomeAfterLogin() 会调用 Home.Activate()。
    // Feeding/Growth/Mine 懒加载：用户切到对应 Tab 时才创建。
    // Feeding.EditRequested / Mine.LogoutRequested 事件在首次创建时订阅。
    public HomeViewModel Home { get; }
    private FeedingViewModel? _feeding;
    private GrowthViewModel? _growth;
    private MineViewModel? _mine;

    public FeedingViewModel Feeding
    {
        get
        {
            if (_feeding is null)
            {
                _feeding = new FeedingViewModel();
                _feeding.EditRequested += OpenEditRecord;
            }
            return _feeding;
        }
    }
    public GrowthViewModel Growth => _growth ??= new GrowthViewModel();
    public MineViewModel Mine
    {
        get
        {
            if (_mine is null)
            {
                _mine = new MineViewModel();
                _mine.LogoutRequested += OnLogout;
            }
            return _mine;
        }
    }

    public event Action? LogoutRequested;

    /// <summary>
    /// 当"是否需要拦截系统返回"状态变化时触发。
    /// Android 端据此动态注册/注销 OnBackInvokedCallback：
    /// 需要拦截时注册（吞掉返回手势关闭弹层/切 Tab），
    /// 不需要时注销（恢复预测式返回动画，用户从边缘滑动可见返回预览）。
    /// </summary>
    public event Action<bool>? InterceptBackChanged;

    /// <summary>构造完成后开启返回拦截状态通知（避免构造期间属性初始化误触发）。</summary>
    private bool _isNotifyingBack;
    private bool _lastInterceptBack;

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

    /// <summary>
    /// 是否需要拦截系统返回：有任何弹层打开、记录抽屉打开、功能菜单展开、或非首页 Tab。
    /// Android 端据此动态注册/注销 OnBackInvokedCallback，避免全程注册导致预测式返回动画失效。
    /// </summary>
    public bool ShouldInterceptBack =>
        _overlays.Any(e => e.IsOpen())
        || IsRecordSheetOpen
        || QuickMenu.IsMenuOpen
        || !IsHomeSelected;

    /// <summary>
    /// 弹层/Tab 状态变化时触发，通知 Android 端动态注册/注销 OnBackInvokedCallback。
    /// 仅当 ShouldInterceptBack 的值真正变化时才触发事件，避免重复注册/注销。
    /// </summary>
    private void RaiseInterceptBackChanged()
    {
        if (!_isNotifyingBack) return;
        var now = ShouldInterceptBack;
        if (now == _lastInterceptBack) return;
        _lastInterceptBack = now;
        InterceptBackChanged?.Invoke(now);
    }

    // 各弹层 IsXxxOpen 变化时触发拦截状态检查
    partial void OnIsBabySetupOpenChanged(bool value) => RaiseInterceptBackChanged();
    partial void OnIsBabyManagerOpenChanged(bool value) => RaiseInterceptBackChanged();
    partial void OnIsStatisticsOpenChanged(bool value) => RaiseInterceptBackChanged();
    partial void OnIsPointsOpenChanged(bool value) => RaiseInterceptBackChanged();
    partial void OnIsMembershipOpenChanged(bool value) => RaiseInterceptBackChanged();
    partial void OnIsAiAnalysisOpenChanged(bool value) => RaiseInterceptBackChanged();
    partial void OnIsAiSettingsOpenChanged(bool value) => RaiseInterceptBackChanged();
    partial void OnIsSyncSettingsOpenChanged(bool value) => RaiseInterceptBackChanged();
    partial void OnIsFamilyOpenChanged(bool value) => RaiseInterceptBackChanged();
    partial void OnIsDeveloperOptionsOpenChanged(bool value) => RaiseInterceptBackChanged();
    partial void OnIsHelpOpenChanged(bool value) => RaiseInterceptBackChanged();
    partial void OnIsPrivacyPolicyOpenChanged(bool value) => RaiseInterceptBackChanged();
    partial void OnIsInAppMessageOpenChanged(bool value) => RaiseInterceptBackChanged();
    partial void OnIsLanguageSettingsOpenChanged(bool value) => RaiseInterceptBackChanged();

    public MainShellViewModel()
    {
        // ★ 启动埋点 P0：测量 MainShellViewModel 构造耗时（含立即创建的 4 个 VM）
        // 懒加载改造后此处仅创建 Home/RecordSheet/QuickMenu/QuickInput，其余 VM 懒加载
        var ctorSw = System.Diagnostics.Stopwatch.StartNew();
        DevLogger.Log("Startup", "MainShellViewModel ctor start");

        // ===== 启动必需：立即创建 =====
        // Home：构造完成后立即 ActivateHomeAfterLogin() 会调用 Home.Activate()，必须立即可用
        Home = new HomeViewModel();
        // RecordSheet：OpenQuickRecord/OpenEditRecord 可能立即被调用，且 Saved/Closed/VaccineInlineChanged
        //   事件订阅必须在首次使用前完成
        _recordSheet = new RecordSheetViewModel();
        _recordSheet.Saved += OnRecordSaved;
        _recordSheet.Closed += OnRecordSheetClosed;
        _recordSheet.VaccineInlineChanged += OnVaccineInlineChanged;
        // QuickMenu：首页 + 按钮立即可能被点击，且 OnIsRecordSheetOpenChanged 会访问 QuickMenu
        _quickMenu = new QuickMenuViewModel();
        _quickMenu.OpenRecordRequested += OpenQuickRecord;
        // QuickInput：首页底部输入栏立即显示，Saved/MembershipRequired/ToggleActionsRequested/CloseActionsRequested
        //   事件必须在用户交互前订阅
        _quickInput = new QuickInputViewModel();
        _quickInput.Saved += OnRecordSaved;
        // AI 记次数用尽 → 跳转会员中心
        _quickInput.MembershipRequired += OpenMembership;
        // + 按钮点击 → 转发到功能面板展开/收起
        _quickInput.ToggleActionsRequested += () => QuickMenu.ToggleMenuCommand.Execute(null);
        // 输入内容时强制收起功能面板
        _quickInput.CloseActionsRequested += () => QuickMenu.CloseMenuCommand.Execute(null);

        _currentTab = Home;

        // 转发 Home 事件到 Shell 层（立即订阅，避免遗漏）
        Home.StatisticsRequested += OpenStatistics;
        Home.CheckInRequested += OpenPoints;
        Home.QuickRecordRequested += OpenQuickRecord;

        // 订阅 QuickMenu.IsMenuOpen 变化，触发返回拦截状态检查
        QuickMenu.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuickMenuViewModel.IsMenuOpen))
                RaiseInterceptBackChanged();
        };

        // 构造完成，开启返回拦截状态通知并初始化基线
        _lastInterceptBack = ShouldInterceptBack;
        _isNotifyingBack = true;

        ctorSw.Stop();
        DevLogger.Log("Startup", $"MainShellViewModel ctor done: {ctorSw.ElapsedMilliseconds}ms");
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
            "feeding" => Feeding,  // 懒加载：首次切到喂养 Tab 时创建 FeedingViewModel
            "growth" => Growth,    // 懒加载：首次切到成长 Tab 时创建 GrowthViewModel
            "mine" => Mine,        // 懒加载：首次切到我的 Tab 时创建 MineViewModel
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
        // 懒加载场景下用 IsXxxSelected 判断（未创建实例也能正确返回）
        if (IsFeedingSelected) return "feeding";
        if (IsGrowthSelected) return "growth";
        if (IsMineSelected) return "mine";
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
            "feeding" => Feeding,  // 懒加载：恢复时按需创建
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
        BabySetup.Reset();  // 懒加载：首次访问创建实例 + 注册 Overlay
        IsBabySetupOpen = true;
    }

    public async void OpenBabyManager()
    {
        try
        {
            IsBabyManagerOpen = true;
            await BabyManager.LoadAsync();  // 懒加载：首次访问创建实例
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OpenBabyManager failed: " + ex); }
    }

    public async void OpenStatistics()
    {
        try
        {
            IsStatisticsOpen = true;
            await Statistics.LoadAsync();  // 懒加载：首次访问创建实例
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OpenStatistics failed: " + ex); }
    }

    public async void OpenPoints()
    {
        try
        {
            IsPointsOpen = true;
            await Points.LoadAsync();  // 懒加载：首次访问创建实例
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OpenPoints failed: " + ex); }
    }

    /// <summary>打开会员中心页。</summary>
    public async void OpenMembership()
    {
        try
        {
            IsMembershipOpen = true;
            await Membership.LoadAsync();  // 懒加载：首次访问创建实例
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OpenMembership failed: " + ex); }
    }

    /// <summary>会员支付成功后刷新"我的"页会员状态文案。</summary>
    private async void OnMembershipPaymentSucceeded()
    {
        try { await Mine.RefreshMembershipStatusAsync(); }  // Mine 懒加载：若用户未访问过"我的"页则此处创建
        catch (Exception ex) { DevLogger.Log("Shell", "OnMembershipPaymentSucceeded refresh failed: " + ex); }
    }

    public async void OpenAiAnalysis()
    {
        try
        {
            IsAiAnalysisOpen = true;
            await AiAnalysis.LoadAsync();  // 懒加载：首次访问创建实例
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OpenAiAnalysis failed: " + ex); }
    }

    public void OpenAiSettings()
    {
        AiSettings.Activate();  // 懒加载：首次访问创建实例
        IsAiSettingsOpen = true;
    }

    public void OpenReminderSettings()
    {
        ReminderSettings.Activate();  // 懒加载：首次访问创建实例
        IsReminderSettingsOpen = true;
    }

    public void OpenSyncSettings()
    {
        IsSyncSettingsOpen = true;  // 懒加载：SyncSettings 属性在 View 绑定时才创建
    }

    public async void OpenFamily()
    {
        try
        {
            IsFamilyOpen = true;
            await Family.LoadAsync();  // 懒加载：首次访问创建实例
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OpenFamily failed: " + ex); }
    }

    public void OpenDeveloperOptions()
    {
        // 正式版构建隐藏开发者选项入口（MineView 已通过 IsDeveloperOptionsVisible 隐藏），
        // 此处作为运行时双重保险，防止通过其他路径（如自动化测试、深链接）绕过 UI 隐藏。
#if !DEV_BUILD
        return;
#else
        IsDeveloperOptionsOpen = true;
        DeveloperOptions.Activate();  // 懒加载：首次访问创建实例
#endif
    }

    /// <summary>打开"使用帮助"页。</summary>
    public void OpenHelp()
    {
        IsHelpOpen = true;  // 懒加载：Help 属性在 View 绑定时才创建
    }

    /// <summary>打开隐私政策查看（只读模式，不展示同意/不同意按钮）。</summary>
    public void OpenPrivacyPolicy()
    {
        // 直接展示完整协议视图（PrivacyPolicy 属性首次访问时创建实例并注册 Overlay）
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
            await InAppMessage.LoadAsync();  // 懒加载：首次访问创建实例
        }
        catch (Exception ex) { DevLogger.Log("Shell", "OpenInAppMessage failed: " + ex); }
    }

    /// <summary>打开语言设置页（中英文切换）。</summary>
    public void OpenLanguageSettings()
    {
        IsLanguageSettingsOpen = true;  // 懒加载：LanguageSettings 属性在 View 绑定时才创建
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
            // 仅在当前是喂养 Tab 时才刷新列表；否则等用户切过去时 SwitchTab 会自动 Activate
            if (IsFeedingSelected)
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
        // 释放可释放的懒加载 VM（如 SyncSettings 实现 IDisposable）
        (_syncSettings as IDisposable)?.Dispose();
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
