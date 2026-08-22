using System.Collections.ObjectModel;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChildNotes.Services;

/// <summary>
/// 应用全局状态。
/// Family-centric 身份模型（阶段 1C，见 docs/development/family-identity-architecture.md 第 8 节）：
///   - GetLocalDataSpaceId()：本机数据空间 Id，家庭业务表本地 user_id 恒为此值，登录/登出/换绑均不改
///   - GetCloudUserId()：云端账号（JWT 主体），个人数据归属；未登录返回 null
///   - GetCurrentFamilyId()：当前绑定家庭，家庭数据云端归属；未绑定返回 null
///   - GetDeviceId()：设备标识，X-Device-Id / 推送 / 冲突归因；与登录态无关
/// 旧的 UserId（CloudUserId ?? LocalUserId 混合语义）已删除，消费点按 L/C/F/D 分类切换。
/// </summary>
public sealed partial class AppState : ObservableObject
{
    [ObservableProperty] private AppUser? _user;
    [ObservableProperty] private Baby? _currentBaby;
    public ObservableCollection<Baby> BabyList { get; } = new();

    // SyncConfigRepository 在 ServiceProvider 构造时注入；构造 AppState 时尚未就绪，
    // 用回调方式延迟绑定，避免循环依赖（AppState 被多个 service 注入）。
    private SyncConfigRepository? _cfgRepo;

    /// <summary>由 ServiceProvider 构造完成后调用，注入 SyncConfigRepository 供身份计算。</summary>
    public void BindSyncConfigRepository(SyncConfigRepository repo) => _cfgRepo = repo;

    /// <summary>
    /// 本机数据空间 Id（家庭业务表本地 user_id）。首次启动生成，永久不变；
    /// 登录/登出/换绑均不改（换绑只影响云端 FamilyId 归属，不动本地数据可见性）。
    /// </summary>
    public string GetLocalDataSpaceId()
    {
        var cfg = _cfgRepo?.Get();
        if (cfg is not null && !string.IsNullOrWhiteSpace(cfg.LocalUserId)) return cfg.LocalUserId;
        return User?.Id ?? string.Empty; // 兜底：repo 未绑定（不应发生，ServiceProvider 启动时已生成）
    }

    /// <summary>
    /// 云端账号 Id（个人数据 + 云端身份）。未登录返回 null；
    /// 积分/签到/站内信等个人数据消费点用 ?? GetLocalDataSpaceId() 兜底离线态。
    /// </summary>
    public string? GetCloudUserId()
    {
        var cfg = _cfgRepo?.Get();
        if (cfg is not null) return string.IsNullOrWhiteSpace(cfg.CloudUserId) ? null : cfg.CloudUserId;
        return User?.Id;
    }

    /// <summary>当前绑定家庭 Id（家庭数据云端归属）。未登录/未绑定返回 null。</summary>
    public string? GetCurrentFamilyId()
    {
        var cfg = _cfgRepo?.Get();
        if (cfg is not null) return string.IsNullOrWhiteSpace(cfg.CurrentFamilyId) ? null : cfg.CurrentFamilyId;
        return null;
    }

    /// <summary>设备唯一标识（X-Device-Id / 推送注册 / 冲突归因）。与登录态无关。</summary>
    public string GetDeviceId()
    {
        var cfg = _cfgRepo?.Get();
        return cfg is not null && !string.IsNullOrWhiteSpace(cfg.DeviceId) ? cfg.DeviceId : string.Empty;
    }

    /// <summary>
    /// 个人数据查询身份：已登录用 CloudUserId，未登录离线态挂 LocalDataSpaceId
    /// （登录时由 AdoptPersonalDataOnLogin 归并到账号名下，见设计文档 6.5）。
    /// </summary>
    public string GetPersonalDataUserId() => GetCloudUserId() ?? GetLocalDataSpaceId();

    public string? CurrentBabyId => CurrentBaby?.Id;

    public void Clear()
    {
        User = null;
        CurrentBaby = null;
        BabyList.Clear();
    }
}
