using System.IO;
using ChildNotes.Data;
using ChildNotes.Data.Repositories;
using ChildNotes.Services;
using ChildNotes.Services.Sync;

namespace ChildNotes.Infrastructure;

public sealed class ServiceProvider
{
    public static ServiceProvider Instance { get; } = new();

    public DbConnectionFactory DbFactory { get; }
    public AppState AppState { get; }
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
    public WebDavConfigRepository WebDavConfigRepository { get; }
    public SyncService SyncService { get; }
    public SyncTrigger SyncTrigger { get; }

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
        AuthService = new AuthService(new UserRepository(DbFactory), AppState);
        BabyService = new BabyService(new BabyRepository(DbFactory), AppState);
        RecordService = new RecordService(new RecordRepository(DbFactory), AppState);
        StatisticsService = new StatisticsService(RecordService);
        PointsRepository = new PointsRepository(DbFactory);
        PointsService = new PointsService(PointsRepository, RecordService, AppState);
        MilestoneRepository = new MilestoneRepository(DbFactory);
        UploadService = new UploadService(imageDir);
        AiAnalysisRepository = new AiAnalysisRepository(DbFactory);
        LlmClient = new LlmClient();
        AiAnalysisService = new AiAnalysisService(AiAnalysisRepository, RecordService, BabyService, AppState, LlmClient);

        WebDavConfigRepository = new WebDavConfigRepository(DbFactory);
        SyncService = new SyncService(DbFactory, WebDavConfigRepository, dbPath, imageDir);
        SyncTrigger = new SyncTrigger(SyncService);
        DevLogger.Log("DI", "ServiceProvider ctor done");
    }

    public void BindUserToState()
    {
        AppState.User = AuthService.CurrentUser;
        DevLogger.Log("DI", $"BindUserToState: user={AppState.User?.Username}, id={AppState.User?.Id}");
    }
}
