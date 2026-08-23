using System;
using Android.Provider;

namespace ChildNotes.Android.Services;

/// <summary>
/// ANDROID_ID 提供者（Family-centric 阶段 2，设计文档第 4 节）。
/// 在进程级 Application.OnCreate 注入（早于 Avalonia / ServiceProvider 构造），
/// 供 DeviceId / LocalDataSpaceId 的 SHA256 派生使用（同设备重装后 Id 连续）。
/// 读取失败返回 null：上层回退 GUID 派生（与桌面端行为一致）。
/// </summary>
internal sealed class AndroidDeviceIdentityProvider : ChildNotes.Infrastructure.IDeviceIdentityProvider
{
    private readonly Application _app;

    public AndroidDeviceIdentityProvider(Application app) => _app = app;

    public string? GetAndroidId()
    {
        try
        {
            return Settings.Secure.GetString(_app.ContentResolver, Settings.Secure.AndroidId);
        }
        catch (Exception)
        {
            // 极少数 ROM 上 ContentResolver 异常：回退 GUID 派生
            return null;
        }
    }
}
