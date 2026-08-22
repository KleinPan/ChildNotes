using System.Security.Cryptography;
using System.Text;

namespace ChildNotes.Infrastructure;

/// <summary>
/// 平台设备标识提供者（Family-centric 阶段 2，设计文档第 4 节）。
/// Android 在进程级 Application.OnCreate 注入实现（读 Settings.Secure.ANDROID_ID），
/// 先于 Avalonia 启动 / ServiceProvider 构造，确保 EnsureDeviceId / EnsureLocalUserId
/// 派生时可用。默认 null：桌面端 / iOS 未注入时回退 GUID（与既有行为一致）。
/// </summary>
public interface IDeviceIdentityProvider
{
    /// <summary>返回平台稳定设备标识（ANDROID_ID）；不可用时返回 null。</summary>
    string? GetAndroidId();
}

/// <summary>进程级提供者注册点（静态，无 DI 依赖，供平台头部项目启动时注入）。</summary>
public static class DeviceIdentityProvider
{
    private static volatile IDeviceIdentityProvider? _current;

    /// <summary>当前注册的平台提供者；null = 未注入（回退 GUID 派生）。</summary>
    public static IDeviceIdentityProvider? Current
    {
        get => _current;
        set => _current = value;
    }
}

/// <summary>
/// DeviceId / LocalDataSpaceId 派生规则（设计文档第 4 节）：
///
///   DeviceId         = 既有值 ?? SHA256("childnotes-device-v1:"       + ANDROID_ID) ?? GUID
///   LocalDataSpaceId = 既有值 ?? SHA256("childnotes-local-user-v1:"   + ANDROID_ID) ?? GUID
///
/// 不变量：sync_config 已有非空值 → 永远直接使用（写入后冻结）；
/// 为空 → 平台标识可用则派生（同设备重装后可恢复原 Id，数据归属连续），否则 GUID。
/// </summary>
public static class DeviceIdentityDerivation
{
    private const string DeviceIdPrefix = "childnotes-device-v1:";
    private const string LocalDataSpaceIdPrefix = "childnotes-local-user-v1:";

    /// <summary>派生 DeviceId（设备唯一标识，冲突归因用）。</summary>
    public static string DeriveDeviceId(string? androidId) => Derive(DeviceIdPrefix, androidId);

    /// <summary>派生 LocalDataSpaceId（本机数据空间 Id，家庭业务表本地 user_id）。</summary>
    public static string DeriveLocalDataSpaceId(string? androidId) => Derive(LocalDataSpaceIdPrefix, androidId);

    private static string Derive(string prefix, string? androidId)
    {
        if (string.IsNullOrWhiteSpace(androidId)) return Guid.NewGuid().ToString("N");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(prefix + androidId));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
