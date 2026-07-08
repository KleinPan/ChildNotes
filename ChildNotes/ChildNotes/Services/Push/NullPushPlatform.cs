namespace ChildNotes.Services.Push;

/// <summary>
/// 推送平台的空实现：未接入任何推送 SDK 时使用。
/// Desktop 平台默认使用此实现（桌面端无需推送）。
///
/// 后续 Android/iOS 平台接入真实推送 SDK 后，替换为平台实现即可，
/// 业务代码无需任何修改。
/// </summary>
public sealed class NullPushPlatform : IPushPlatform
{
    public string PlatformId => "desktop";
    public bool IsAvailable => false;
    public string? CurrentToken => null;

    public Task InitializeAsync() => Task.CompletedTask;

    public event Action<string, string?, IReadOnlyDictionary<string, string>?>? NotificationReceived
    { add { } remove { } }
    public event Action<IReadOnlyDictionary<string, string>>? NotificationTapped
    { add { } remove { } }
}

/// <summary>
/// 本地通知的空实现：Desktop 平台或权限未授予时使用。
/// </summary>
public sealed class NullLocalNotification : ILocalNotification
{
    public bool IsSupported => false;
    public Task<bool> RequestPermissionAsync() => Task.FromResult(false);
    public Task ScheduleAsync(string id, string title, string body, DateTime fireAt,
        IReadOnlyDictionary<string, string>? data = null) => Task.CompletedTask;
    public Task CancelAsync(string id) => Task.CompletedTask;
    public Task CancelAllAsync() => Task.CompletedTask;
}
