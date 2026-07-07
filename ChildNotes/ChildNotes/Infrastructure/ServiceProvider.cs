using System.IO;
using Avalonia.Controls;
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
    public UserRepository UserRepository { get; }
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

    /// <summary>
    /// 主窗口引用：用于在 ViewModel 中获取 TopLevel.Clipboard 等平台能力。
    /// 由 MainWindow 构造完成后赋值；VM 在调用前判空即可。
    /// </summary>
    public TopLevel? MainView { get; set; }

    private ServiceProvider()
    {
        DevLogger.Log("DI", "ServiceProvider ctor start");
        var appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChildNotes");
        Directory.CreateDirectory(appDir);
        DevLogger.Log("DI", $"appDir={appDir}");

        var dbPath = Path.Combine(appDir, "childnotes.db");
        // 项目未正式上线，不维护兼容性。检测到旧 schema（业务表 id 为 INTEGER 而非 TEXT）
        // 直接删除整个 DB 文件重建。背景：commit 6b5b616 起全栈 ID 改为 GUID 字符串，
        // 但 SQLite 的 CREATE TABLE IF NOT EXISTS 不会更新已存在表的列类型，老库的
        // app_user/baby/child_record 等表 id 仍是 INTEGER PRIMARY KEY，写入 Guid 字符串
        // （含 a-f 字母）会触发 "datatype mismatch (error 20)"。
        EnsureSchemaVersion(dbPath);
        DbFactory = new DbConnectionFactory(dbPath);
        DbInitializer.Initialize(DbFactory);

        var imageDir = Path.Combine(appDir, "images");

        // SyncConfigRepository 提前初始化：UploadService 依赖它做异步上传
        SyncConfigRepository = new SyncConfigRepository(DbFactory);
        EnsureDeviceId();

        AppState = new AppState();
        UserRepository = new UserRepository(DbFactory);
        var babyRepo = new BabyRepository(DbFactory);
        var recordRepo = new RecordRepository(DbFactory);
        SessionRepository = new SessionRepository(DbFactory);
        AuthService = new AuthService(UserRepository, SessionRepository, AppState, SyncConfigRepository);
        BabyService = new BabyService(babyRepo, AppState);
        RecordService = new RecordService(recordRepo, AppState);
        StatisticsService = new StatisticsService(RecordService);
        PointsRepository = new PointsRepository(DbFactory);
        PointsService = new PointsService(PointsRepository, RecordService, AppState);
        MilestoneRepository = new MilestoneRepository(DbFactory);
        UploadService = new UploadService(imageDir, SyncConfigRepository);
        AiAnalysisRepository = new AiAnalysisRepository(DbFactory);
        LlmClient = new LlmClient();
        AiAnalysisService = new AiAnalysisService(AiAnalysisRepository, RecordService, BabyService, AppState, LlmClient);

        NetworkMonitor = new NetworkMonitor();
        ApiSyncService = new ApiSyncService(SyncConfigRepository, babyRepo, recordRepo, MilestoneRepository, DbFactory);
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

    /// <summary>
    /// 检测 DB schema 版本，若与当前期望不符则删除整个 DB 文件让其重建。
    /// 项目未正式上线，不做数据迁移，直接重建最稳妥。
    /// 当前期望：业务表 child_record.id 列类型为 TEXT。若为 INTEGER（旧版 schema）则重建。
    /// </summary>
    private static void EnsureSchemaVersion(string dbPath)
    {
        if (!File.Exists(dbPath)) return;

        bool needRebuild = false;
        // 用独立 using 块确保连接在判断 needRebuild 前完全释放（含 Pooling=False 不入池）
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            // child_record 是核心业务表，id 列类型反映 schema 版本
            cmd.CommandText = "PRAGMA table_info(child_record);";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (r.GetString(1) == "id" && r.GetString(2).Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
                {
                    needRebuild = true;
                    break;
                }
            }
        }
        // 连接已 Dispose，文件句柄释放；清池兜底（Pooling=False 不入池，但旧残留无害）
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (needRebuild)
        {
            DevLogger.Log("DI", "Schema outdated (child_record.id is INTEGER), rebuilding DB.");
            // SQLite 启用 WAL 模式时会有 -wal 和 -shm 旁路文件，需一起处理
            var wal = dbPath + "-wal";
            var shm = dbPath + "-shm";
            // 删除可能因文件句柄残留失败，重试 3 次（每次间隔递增）
            bool deleted = false;
            for (int i = 0; i < 3 && !deleted; i++)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try
                {
                    if (File.Exists(dbPath)) File.Delete(dbPath);
                    if (File.Exists(wal)) File.Delete(wal);
                    if (File.Exists(shm)) File.Delete(shm);
                    deleted = true;
                }
                catch (Exception) when (i < 2)
                {
                    System.Threading.Thread.Sleep(200 * (i + 1));
                }
            }
            // 仍删除失败时兜底：把旧文件改名为 .old，让 DbInitializer 用新 schema 建新 DB
            if (!deleted)
            {
                try
                {
                    var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    if (File.Exists(dbPath)) File.Move(dbPath, $"{dbPath}.{stamp}.old");
                    if (File.Exists(wal)) File.Move(wal, $"{wal}.{stamp}.old", overwrite: true);
                    if (File.Exists(shm)) File.Move(shm, $"{shm}.{stamp}.old", overwrite: true);
                    DevLogger.Log("DI", "DB file delete failed, renamed old files with .old suffix.");
                }
                catch (Exception ex)
                {
                    // 连 rename 都失败——DB 文件被严重锁定，无法继续。抛出明确异常便于排查
                    throw new InvalidOperationException(
                        "无法删除/重命名旧的数据库文件（schema 不兼容）。请关闭所有占用该文件的程序后重试。" +
                        $"路径: {dbPath}。原因: {ex.Message}", ex);
                }
            }
        }
    }
}
