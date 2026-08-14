using System.Collections.ObjectModel;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChildNotes.Services;

/// <summary>
/// 应用全局状态。
/// v5 schema 重构：
///   - UserId 改为从 SyncConfigRepository 实时读取：
///     已登录返回 CloudUserId，未登录返回 LocalUserId（首次启动生成的 GUID）。
///   - User 仅在登录后非空（缓存 profile）；未登录时 User 为 null 但 UserId 仍有值。
/// </summary>
public sealed partial class AppState : ObservableObject
{
    [ObservableProperty] private AppUser? _user;
    [ObservableProperty] private Baby? _currentBaby;
    public ObservableCollection<Baby> BabyList { get; } = new();

    // SyncConfigRepository 在 ServiceProvider 构造时注入；构造 AppState 时尚未就绪，
    // 用回调方式延迟绑定，避免循环依赖（AppState 被多个 service 注入）。
    private SyncConfigRepository? _cfgRepo;

    /// <summary>由 ServiceProvider 构造完成后调用，注入 SyncConfigRepository 供 UserId 计算。</summary>
    public void BindSyncConfigRepository(SyncConfigRepository repo) => _cfgRepo = repo;

    /// <summary>
    /// 当前用户 Id：
    /// - 已登录（CloudUserId 非空）→ 返回 CloudUserId
    /// - 未登录 → 返回 LocalUserId（首次启动生成）
    /// - 极端兜底：LocalUserId 也为空时返回临时 GUID（不应发生，ServiceProvider 已在启动时生成）
    /// </summary>
    public string UserId
    {
        get
        {
            var cfg = _cfgRepo?.Get();
            if (cfg is null) return User?.Id ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(cfg.CloudUserId)) return cfg.CloudUserId;
            if (!string.IsNullOrWhiteSpace(cfg.LocalUserId)) return cfg.LocalUserId;
            return User?.Id ?? string.Empty;
        }
    }

    public string? CurrentBabyId => CurrentBaby?.Id;

    public void Clear()
    {
        User = null;
        CurrentBaby = null;
        BabyList.Clear();
    }
}
