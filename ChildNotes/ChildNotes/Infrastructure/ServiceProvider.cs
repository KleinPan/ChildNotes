using System.IO;
using ChildNotes.Data;
using ChildNotes.Data.Repositories;
using ChildNotes.Services;

namespace ChildNotes.Infrastructure;

public sealed class ServiceProvider
{
    public static ServiceProvider Instance { get; } = new();

    public DbConnectionFactory DbFactory { get; }
    public AppState AppState { get; }
    public SessionRepository SessionRepository { get; }
    public AuthService AuthService { get; }
    public BabyService BabyService { get; }
    public RecordService RecordService { get; }
    public StatisticsService StatisticsService { get; }
    public PointsRepository PointsRepository { get; }
    public PointsService PointsService { get; }
    public MilestoneRepository MilestoneRepository { get; }
    public UploadService UploadService { get; }
    public AiAnalysisRepository AiAnalysisRepository { get; }
    public LlmClient LlmClient { get; }
    public AiAnalysisService AiAnalysisService { get; }
    public SyncConfigRepository SyncConfigRepository { get; }
    public ApiSyncService ApiSyncService { get; }
    public SyncTrigger SyncTrigger { get; }
    public NetworkMonitor NetworkMonitor { get; }
    public FamilyApiClient FamilyApiClient { get; }
    public AiParseApiClient AiParseApiClient { get; }
    public IDateTimeFormatter DateTimeFormatter { get; }

    private ServiceProvider()
    {
        DevLogger.Log("DI", "ServiceProvider ctor start");
        var appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChildNotes");
        Directory.CreateDirectory(appDir);
        DevLogger.Log("DI", $"appDir={appDir}");

        var dbPath = Path.Combine(appDir, "childnotes.db");
        DbFactory = new DbConnectionFactory(dbPath);
        DbInitializer.Initialize(DbFactory);

        var imageDir = Path.Combine(appDir, "images");

        AppState = new AppState();
        var userRepo = new UserRepository(DbFactory);
        var babyRepo = new BabyRepository(DbFactory);
        var recordRepo = new RecordRepository(DbFactory);
        SessionRepository = new SessionRepository(DbFactory);
        AuthService = new AuthService(userRepo, SessionRepository, AppState);
        BabyService = new BabyService(babyRepo, AppState);
        RecordService = new RecordService(recordRepo, AppState);
        StatisticsService = new StatisticsService(RecordService);
        PointsRepository = new PointsRepository(DbFactory);
        PointsService = new PointsService(PointsRepository, RecordService, AppState);
        MilestoneRepository = new MilestoneRepository(DbFactory);
        UploadService = new UploadService(imageDir);
        AiAnalysisRepository = new AiAnalysisRepository(DbFactory);
        LlmClient = new LlmClient();
        AiAnalysisService = new AiAnalysisService(AiAnalysisRepository, RecordService, BabyService, AppState, LlmClient);

        SyncConfigRepository = new SyncConfigRepository(DbFactory);
        EnsureDeviceId();
        NetworkMonitor = new NetworkMonitor();
        ApiSyncService = new ApiSyncService(SyncConfigRepository, babyRepo, recordRepo, DbFactory);
        ApiSyncService.NetworkMonitor = NetworkMonitor;
        SyncTrigger = new SyncTrigger(ApiSyncService);
        SyncTrigger.NetworkMonitor = NetworkMonitor;
        NetworkMonitor.StateChanged += SyncTrigger.OnNetworkStateChanged;
        // 注入回写触发，避免循环依赖
        RecordService.SyncTrigger = SyncTrigger;
        BabyService.SyncTrigger = SyncTrigger;
        FamilyApiClient = new FamilyApiClient(SyncConfigRepository);
        AiParseApiClient = new AiParseApiClient(SyncConfigRepository);
        DateTimeFormatter = new DateTimeFormatterService();

        DevLogger.Log("DI", "ServiceProvider ctor done");
    }

    /// <summary>
    /// 首次启动时为 sync_config 生成 device_id（设备唯一标识，用于冲突归因）。
    /// 已存在则跳过。
    /// </summary>
    private void EnsureDeviceId()
    {
        var cfg = SyncConfigRepository.Get();
        if (string.IsNullOrWhiteSpace(cfg.DeviceId))
        {
            cfg.DeviceId = Guid.NewGuid().ToString("N");
            SyncConfigRepository.UpdateDeviceId(cfg.DeviceId);
            DevLogger.Log("DI", $"device_id generated: {cfg.DeviceId}");
        }
    }

    public void BindUserToState()
    {
        AppState.User = AuthService.CurrentUser;
        DevLogger.Log("DI", $"BindUserToState: user={AppState.User?.Username}, id={AppState.User?.Id}");
    }
}
