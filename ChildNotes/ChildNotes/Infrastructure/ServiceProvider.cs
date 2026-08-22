using System.IO;
using Avalonia.Controls;
using ChildNotes.Data;
using ChildNotes.Data.Repositories;
using ChildNotes.Services;
using ChildNotes.Services.Push;
using ChildNotes.Services.Storage;

namespace ChildNotes.Infrastructure;

public sealed class ServiceProvider
{
    public static ServiceProvider Instance { get; } = new();

    public DbConnectionFactory DbFactory { get; }
    public AppState AppState { get; }
    public UserRepository UserRepository { get; }
    public AuthService AuthService { get; private set; }
    /// <summary>
    /// 平台安全存储：默认 DpapiSecureStorage（Windows DPAPI），
    /// Android/iOS 平台启动时通过 OverrideSecureStorage 注入平台实现
    /// （Android Keystore / iOS Keychain）。
    /// AccessToken/RefreshToken 不再以明文保存到 SQLite。
    /// </summary>
    public ISecureStorage SecureStorage { get; private set; }
    public BabyService BabyService { get; }
    public RecordService RecordService { get; }
    public StatisticsService StatisticsService { get; }
    public PointsRepository PointsRepository { get; }
    public PointsService PointsService { get; }
    public SupplementItemRepository SupplementItemRepository { get; }
    public MilestoneRepository MilestoneRepository { get; }
    public UploadService UploadService { get; }
    public AiAnalysisRepository AiAnalysisRepository { get; }
    public LlmClient LlmClient { get; }
    public AiAnalysisService AiAnalysisService { get; }
    public SyncConfigRepository SyncConfigRepository { get; }
    public SyncLogRepository SyncLogRepository { get; }
    public ReminderConfigRepository ReminderConfigRepository { get; }
    public ApiSyncService ApiSyncService { get; }
    public SyncTrigger SyncTrigger { get; }
    public NetworkMonitor NetworkMonitor { get; }
    public FamilyApiClient FamilyApiClient { get; }
    public AiParseApiClient AiParseApiClient { get; }
    public AiAnalysisApiClient AiAnalysisApiClient { get; }
    public MembershipApiClient MembershipApiClient { get; }
    public PointsApiClient PointsApiClient { get; }
    public IDateTimeFormatter DateTimeFormatter { get; }
    public Data.Repositories.InAppMessageRepository InAppMessageRepository { get; }
    public Services.InAppMessageService InAppMessageService { get; }
    public Services.ReminderService ReminderService { get; }
    public Services.Push.IPushPlatform PushPlatform { get; }
    /// <summary>本地通知：默认 NullLocalNotification，Android/iOS 平台启动时通过 OverrideLocalNotification 注入真实实现。</summary>
    public Services.Push.ILocalNotification LocalNotification { get; private set; }
    public Services.Push.IPushService PushService { get; }
    /// <summary>
    /// 图片选择器：默认 DesktopPhotoPicker（用 Avalonia StorageProvider 系统文件对话框），
    /// Android 平台在 MainActivity.OnCreate 中通过 OverridePhotoPicker 注入 AndroidPhotoPicker
    /// （调起系统相册网格 Photo Picker，无需任何运行时权限）。
    /// </summary>
    public Services.PhotoPicker.IPhotoPicker PhotoPicker { get; private set; }
    /// <summary>家庭加入申请本地仓储（Pull-only，同步审批状态 + 生成本地通知）。</summary>
    public Data.Repositories.FamilyJoinRequestRepository FamilyJoinRequestRepository { get; }

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
        SyncLogRepository = new SyncLogRepository(DbFactory);
        EnsureDeviceId();

        AppState = new AppState();
        // SecureStorage 默认用 Windows DPAPI（桌面端调试用）；Android/iOS 在平台启动时通过 OverrideSecureStorage 注入
        SecureStorage = new DpapiSecureStorage();
        UserRepository = new UserRepository(DbFactory);
        var babyRepo = new BabyRepository(DbFactory);
        var recordRepo = new RecordRepository(DbFactory);
        AuthService = new AuthService(UserRepository, AppState, SyncConfigRepository, SecureStorage);
        // AppState 需要读 SyncConfigRepository 计算 UserId（已登录=CloudUserId / 未登录=LocalUserId）
        AppState.BindSyncConfigRepository(SyncConfigRepository);
        BabyService = new BabyService(babyRepo, AppState);
        RecordService = new RecordService(recordRepo, AppState);
        StatisticsService = new StatisticsService(RecordService);
        PointsRepository = new PointsRepository(DbFactory);
        PointsService = new PointsService(PointsRepository, RecordService, AppState);
        SupplementItemRepository = new SupplementItemRepository(DbFactory);
        MilestoneRepository = new MilestoneRepository(DbFactory);
        UploadService = new UploadService(imageDir, SyncConfigRepository);
        AiAnalysisRepository = new AiAnalysisRepository(DbFactory);
        LlmClient = new LlmClient();
        AiAnalysisApiClient = new AiAnalysisApiClient(SyncConfigRepository);
        MembershipApiClient = new MembershipApiClient(SyncConfigRepository);
        PointsApiClient = new PointsApiClient(SyncConfigRepository);
        AiAnalysisService = new AiAnalysisService(AiAnalysisRepository, RecordService, BabyService, AppState, LlmClient, AiAnalysisApiClient);

        NetworkMonitor = new NetworkMonitor();
        ApiSyncService = new ApiSyncService(SyncConfigRepository, babyRepo, recordRepo, MilestoneRepository, PointsRepository, DbFactory);
        ApiSyncService.NetworkMonitor = NetworkMonitor;
        SyncTrigger = new SyncTrigger(ApiSyncService, SyncLogRepository);
        SyncTrigger.NetworkMonitor = NetworkMonitor;
        NetworkMonitor.StateChanged += SyncTrigger.OnNetworkStateChanged;
        // 注入回写触发，避免循环依赖
        RecordService.SyncTrigger = SyncTrigger;
        BabyService.SyncTrigger = SyncTrigger;
        // 本地提醒配置仓储：供 ReminderService 读取阈值、ReminderSettingsViewModel 读写配置
        ReminderConfigRepository = new ReminderConfigRepository(DbFactory);
        // 本地提醒服务：依赖 RecordService（反向注入避免循环依赖，与 SyncTrigger 模式一致）
        ReminderService = new Services.ReminderService(RecordService, ReminderConfigRepository);
        RecordService.ReminderService = ReminderService;
        FamilyApiClient = new FamilyApiClient(SyncConfigRepository);
        AiParseApiClient = new AiParseApiClient(SyncConfigRepository);
        DateTimeFormatter = new DateTimeFormatterService();

        // 应用内消息（轻量推送替代）
        // 注：属性名与类型名相同，需用完整命名空间限定类型
        InAppMessageRepository = new Data.Repositories.InAppMessageRepository(DbFactory);
        InAppMessageService = new Services.InAppMessageService(InAppMessageRepository, AppState);

        // 家庭加入申请本地仓储（Pull-only，用于同步审批状态 + 生成本地通知）
        // 注：属性名 FamilyJoinRequestRepository 与类型 Data.Repositories.FamilyJoinRequestRepository 同名，
        // 此处用完整命名空间限定类型，避免 using ChildNotes.Data.Repositories 引入的歧义。
        FamilyJoinRequestRepository = new Data.Repositories.FamilyJoinRequestRepository(DbFactory);
        // 注入 ApiSyncService 的 join_request 依赖（InAppMessageService + AppState + 仓储）
        ApiSyncService.SetJoinRequestDeps(this.FamilyJoinRequestRepository, InAppMessageService, AppState);

        // 推送平台：默认 NullPushPlatform（未接入 SDK），后续 Android/iOS 平台替换为真实实现
        PushPlatform = new Services.Push.NullPushPlatform();
        // 本地通知：默认 NullLocalNotification；Android 平台在 MainActivity.OnCreate 中
        // 调用 ServiceProvider.Instance.OverrideLocalNotification(new AndroidLocalNotification()) 注入
        LocalNotification = new Services.Push.NullLocalNotification();
        PushService = new Services.Push.PushApiClient(SyncConfigRepository);

        // 图片选择器：默认 DesktopPhotoPicker（桌面端系统文件对话框）。
        // TopLevel 延迟取值：调用 PickImageAsync 时才从当前控件取，避免构造时主窗口尚未建立。
        // Android 平台在 MainActivity.OnCreate 中通过 OverridePhotoPicker 注入 AndroidPhotoPicker。
        PhotoPicker = new Services.PhotoPicker.DesktopPhotoPicker(() => MainView);

        // v5：首次启动生成 local_user_id（离线模式业务数据的 user_id，永久不变）。
        // 已存在则跳过。必须在 AuthService 构造之后调用（AuthService.EnsureLocalUserId 内部会读 SyncConfigRepository）。
        AuthService.EnsureLocalUserId();

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
        DevLogger.Log("DI", $"BindUserToState: user={AppState.User?.Email}, id={AppState.User?.Id}, userId={AppState.UserId}");
    }

    /// <summary>
    /// 运行时注入平台安全存储实现。
    /// 由 Android MainActivity.OnCreate 调用，覆盖默认的 DpapiSecureStorage（Windows DPAPI）。
    /// Android 实现使用 Android Keystore（AES-256-GCM，密钥不可导出）。
    /// 注入方式为 AuthService.UpdateSecureStorage 热替换（不重建实例，保持 CurrentUser 等状态）。
    /// </summary>
    public void OverrideSecureStorage(ISecureStorage implementation)
    {
        SecureStorage = implementation;
        // ★ 热替换存储实现，不重建 AuthService 实例：
        // Android 启动时序中 App.OnFrameworkInitializationCompleted（含 TryRestoreSessionAsync，
        // 恢复 CurrentUser）在进程级 Application.OnCreate 中执行，早于 MainActivity.OnCreate 的
        // 本方法。若在此重建 AuthService，已恢复的 CurrentUser 会随旧实例一起丢弃，
        // 导致"我的"页显示"已登录"占位但不显示账号信息（CurrentUser=null + CloudUserId 非空），
        // 且 AppState.User 为 null，账号中心等信息全部消失。
        AuthService.UpdateSecureStorage(implementation);
        AppState.BindSyncConfigRepository(SyncConfigRepository);
        DevLogger.Log("DI", $"SecureStorage overridden: {implementation.GetType().Name}");
    }

    /// <summary>
    /// 运行时注入平台本地通知实现。
    /// 由 Android MainActivity.OnCreate / iOS AppDelegate.FinishedLaunching 调用，
    /// 在 ServiceProvider 构造完成后覆盖默认的 NullLocalNotification。
    /// </summary>
    public void OverrideLocalNotification(Services.Push.ILocalNotification implementation)
    {
        LocalNotification = implementation;
        DevLogger.Log("DI", $"LocalNotification overridden: {implementation.GetType().Name}, IsSupported={implementation.IsSupported}");
    }

    /// <summary>
    /// 运行时注入平台图片选择器实现。
    /// 由 Android MainActivity.OnCreate 调用，覆盖默认的 DesktopPhotoPicker。
    /// Android 实现使用 AndroidX PickVisualMedia（Android 13+ 原生相册网格，
    /// 13- 自动回退 ACTION_OPEN_DOCUMENT），无需任何运行时权限。
    /// </summary>
    public void OverridePhotoPicker(Services.PhotoPicker.IPhotoPicker implementation)
    {
        PhotoPicker = implementation;
        DevLogger.Log("DI", $"PhotoPicker overridden: {implementation.GetType().Name}");
    }

    /// <summary>
    /// 检测 DB schema 版本，若与当前期望不符则删除整个 DB 文件让其重建。
    /// 项目未正式上线，不做数据迁移，直接重建最稳妥（v5 重构删除旧 username/password_hash 列，
    /// SQLite 不支持 DROP UNIQUE 列，迁移路径无法走 ALTER TABLE DROP COLUMN）。
    ///
    /// 重建判定原则（严格）：
    ///   - 只有"明确检测到不支持的旧版本"才允许删除重建
    ///   - 数据库损坏/读取失败/临时 IO 异常/未知异常 → 不删除，向上抛出
    ///   - 否则可能出现：DB 临时 IO 异常 → 被误判为旧版本 → 删除用户全部数据
    ///
    /// 设计原则（2026-08-13 修复）：
    ///   - **不再整体删除数据库**！旧逻辑只要 user_version < CurrentSchemaVersion 就 File.Delete，
    ///     导致用户升级时本地数据全部丢失（llm_config / user_supplement_item 等纯本地数据无法恢复）。
    ///   - 现在改为：检测到旧版本时，**先备份**到 .before_migrate，然后**让 DbInitializer 做增量迁移**
    ///     （DbInitializer 用 CREATE TABLE IF NOT EXISTS / AddColumnIfNotExists / DropColumnIfExists，
    ///     对已存在的表只做列级变更，不会删除数据）。
    ///   - 仅在数据库文件**无法打开**或**损坏**（PRAGMA 读取失败）时，才走兜底重建：
    ///     先备份到 .corrupt.{timestamp}，再删除让 DbInitializer 建新库。
    ///
    /// 旧版本检测（仅用于决定是否备份，不再触发删除）：
    ///   1) PRAGMA user_version 可正常读取，且 &lt; CurrentSchemaVersion（明确的旧版本号）
    ///   2) child_record 表存在且 id 列类型为 INTEGER（极旧版 schema 二次确认）
    /// </summary>
    private static void EnsureSchemaVersion(string dbPath)
    {
        if (!File.Exists(dbPath)) return;

        bool needMigration = false;
        bool isCorrupt = false;
        // 用独立 using 块确保连接在判断前完全释放（含 Pooling=False 不入池）
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False");
            try
            {
                conn.Open();
            }
            catch (Exception ex)
            {
                // DB 文件存在但无法打开：可能损坏/被锁定/暂态 IO 错误
                // 不能误判为旧版本直接删除，向上抛出让用户感知
                throw new InvalidOperationException(
                    "数据库文件无法打开（可能已损坏或被其他进程锁定）。" +
                    $"请勿在 App 运行时手动操作 DB 文件。路径: {dbPath}。原因: {ex.Message}", ex);
            }

            try
            {
                using var verCmd = conn.CreateCommand();
                verCmd.CommandText = "PRAGMA user_version;";
                var curVer = Convert.ToInt32(verCmd.ExecuteScalar() ?? 0, System.Globalization.CultureInfo.InvariantCulture);
                if (curVer < DbInitializer.CurrentSchemaVersion)
                {
                    needMigration = true;
                    DevLogger.Log("DI", $"Schema outdated (user_version={curVer} < {DbInitializer.CurrentSchemaVersion}), will migrate (not rebuild).");
                }
                if (!needMigration)
                {
                    using var cmd = conn.CreateCommand();
                    // child_record 是核心业务表，id 列类型反映极旧版 schema（user_version 可能不准）
                    cmd.CommandText = "PRAGMA table_info(child_record);";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        if (r.GetString(1) == "id" && r.GetString(2).Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
                        {
                            needMigration = true;
                            DevLogger.Log("DI", "Schema outdated (child_record.id is INTEGER), will migrate.");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // PRAGMA 读取失败：DB 可能损坏（file is not a database / database disk image is malformed 等）
                // 这种情况才走兜底重建：先备份，再删除让 DbInitializer 建新库
                DevLogger.Log("DI", $"DB corrupt detected, will backup and rebuild: {ex.Message}");
                isCorrupt = true;
            }
        }
        // 连接已 Dispose，文件句柄释放；清池兜底（Pooling=False 不入池，但旧残留无害）
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // 损坏兜底：先备份，再删除让 DbInitializer 建新库
        if (isCorrupt)
        {
            BackupAndRemoveDb(dbPath, ".corrupt." + DateTime.Now.ToString("yyyyMMddHHmmss"));
            return;
        }

        // 旧版本：先备份到 .before_migrate，然后让 DbInitializer 做增量迁移（不删除！）
        if (needMigration)
        {
            try
            {
                var backupPath = dbPath + ".before_migrate";
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Copy(dbPath, backupPath, overwrite: true);
                DevLogger.Log("DI", $"DB backed up to {backupPath} before migration.");
            }
            catch (Exception ex)
            {
                DevLogger.Log("DI", $"DB backup before migration failed (non-fatal): {ex.Message}");
            }
            // 不删除！让 DbInitializer 增量迁移
        }
    }

    /// <summary>
    /// 兜底重建：数据库文件严重损坏时，先备份到 {dbPath}.{suffix}，再删除让 DbInitializer 建新库。
    /// 仅在 PRAGMA user_version 读取失败（文件损坏）时调用。
    /// </summary>
    private static void BackupAndRemoveDb(string dbPath, string suffix)
    {
        var wal = dbPath + "-wal";
        var shm = dbPath + "-shm";

        // 先备份（即使损坏也保留原文件供用户手动恢复）
        try
        {
            var backupPath = dbPath + suffix;
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Copy(dbPath, backupPath, overwrite: true);
            DevLogger.Log("DI", $"Corrupt DB backed up to {backupPath}.");
        }
        catch (Exception ex)
        {
            DevLogger.Log("DI", $"Corrupt DB backup failed (non-fatal): {ex.Message}");
        }

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
                    "无法删除/重命名损坏的数据库文件。请关闭所有占用该文件的程序后重试。" +
                    $"路径: {dbPath}。原因: {ex.Message}", ex);
            }
        }
    }
}
