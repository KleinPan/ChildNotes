using ChildNotes.Core.Common;
using ChildNotes.Core.Config;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Auth;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

public class InviteService : IInviteService
{
    private readonly ChildNotesDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IReferrerCodeUtil _referrer;
    private readonly PointsWalletService _wallet;

    public InviteService(ChildNotesDbContext db, ICurrentUserService current, IReferrerCodeUtil referrer, PointsWalletService wallet)
    {
        _db = db;
        _current = current;
        _referrer = referrer;
        _wallet = wallet;
    }

    public Task<List<InviteRecordDto>> GetInviteRecordsAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        return GetInviteRecordsInternalAsync(uid, ct);
    }

    public async Task BindReferrerAsync(string userId, string referrerId, bool newUser, CancellationToken ct = default)
    {
        if (!newUser || string.IsNullOrWhiteSpace(referrerId)) return;
        var referrerUserId = _referrer.Decode(referrerId);
        if (string.IsNullOrEmpty(referrerUserId) || userId == referrerUserId) return;

        var invited = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (invited is null || invited.ReferrerUserId is not null) return;

        var referrer = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == referrerUserId, ct);
        if (referrer is null) return;

        var exists = await _db.TaskRecords.AnyAsync(
            t => t.TaskType == "invite_register" && t.RelatedUserId == userId, ct);
        if (exists) return;

        var now = DateTime.UtcNow;
        invited.ReferrerUserId = referrerUserId;
        invited.ReferrerBoundAt = now;
        invited.UpdatedAt = now;

        _db.TaskRecords.Add(new TaskRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = referrerUserId,
            TaskType = "invite_register",
            TaskKey = "invite_mom",
            RelatedUserId = userId,
            Points = PointsConstants.InviteRewardPoints,
            Status = StatusConstants.TaskRecord.Completed,
            PayloadJson = "{\"source\":\"referrer_id\"}",
            CreatedAt = now,
            UpdatedAt = now,
        });
        // 事务包裹：积分增加（ExecuteUpdateAsync 立即落库）与任务记录写入必须原子。
        await _db.ExecuteInTransactionAsync(async () =>
        {
            await _wallet.ChangeAsync(referrerUserId, PointsConstants.InviteRewardPoints, ct);
            await _db.SaveChangesAsync(ct);
        }, ct);
    }

    private async Task<List<InviteRecordDto>> GetInviteRecordsInternalAsync(string userId, CancellationToken ct)
    {
        var records = await _db.TaskRecords
            .Where(t => t.UserId == userId && t.TaskType == "invite_register")
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        if (records.Count == 0) return new();
        var invitedIds = records.Select(r => r.RelatedUserId ?? string.Empty)
            .Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var users = await _db.AppUsers.Where(u => invitedIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);
        return records.Select(r =>
        {
            var nick = "用户"; var avatar = "";
            if (!string.IsNullOrEmpty(r.RelatedUserId) && users.TryGetValue(r.RelatedUserId, out var u))
            { nick = u.NickName; avatar = u.AvatarUrl; }
            return new InviteRecordDto
            {
                Id = r.Id,
                InvitedUserId = r.RelatedUserId ?? string.Empty,
                InvitedNickName = nick,
                InvitedAvatarUrl = avatar,
                Points = r.Points,
                CreatedAt = DateTimeFormatter.FormatDateTime(r.CreatedAt),
            };
        }).ToList();
    }
}
