using System.Text.Json;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Data;
using ChildNotes.Shared.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

/// <summary>
/// 成长时刻（里程碑）服务实现。
/// 权限：仅当前用户自己创建的里程碑可读写；babyId 从请求头 X-Baby-Id 解析（对齐小程序契约），
///      若提供则必须在该用户可访问宝宝集合内。
/// 删除采用软删（Deleted=true），同步通道通过 Deleted 字段传递。
/// </summary>
public class MilestoneService : IMilestoneService
{
    private readonly ChildNotesDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IBabyAccessService _babyAccess;
    private readonly IHttpContextAccessor _httpCtx;

    public MilestoneService(ChildNotesDbContext db, ICurrentUserService current,
        IBabyAccessService babyAccess, IHttpContextAccessor httpCtx)
    {
        _db = db;
        _current = current;
        _babyAccess = babyAccess;
        _httpCtx = httpCtx;
    }

    public async Task<List<MilestoneRecordDto>> ListAsync(long? babyId, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var q = _db.Milestones.AsNoTracking().Where(m => m.UserId == uid);
        if (babyId.HasValue)
        {
            await _babyAccess.EnsureAccessAsync(uid, babyId.Value, ct);
            q = q.Where(m => m.BabyId == babyId.Value);
        }
        var list = await q.OrderByDescending(m => m.RecordDate).ThenByDescending(m => m.Id).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<long> AddAsync(MilestoneRecordDto dto, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var babyId = ResolveBabyId();
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new BusinessException("标题不能为空");
        if (babyId.HasValue)
            await _babyAccess.EnsureAccessAsync(uid, babyId.Value, ct);

        var now = DateTime.UtcNow;
        var entity = new Milestone
        {
            UserId = uid,
            BabyId = babyId,
            Title = dto.Title.Trim(),
            Content = string.IsNullOrWhiteSpace(dto.Content) ? null : dto.Content.Trim(),
            RecordDate = ParseDate(dto.Date),
            PhotosJson = SerializePhotos(dto.Photos),
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Milestones.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<bool> UpdateAsync(long id, MilestoneRecordDto dto, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var existing = await _db.Milestones.FirstOrDefaultAsync(m => m.Id == id && m.UserId == uid, ct);
        if (existing is null) return false;
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new BusinessException("标题不能为空");
        var babyId = ResolveBabyId();
        if (babyId.HasValue)
            await _babyAccess.EnsureAccessAsync(uid, babyId.Value, ct);

        existing.Title = dto.Title.Trim();
        existing.Content = string.IsNullOrWhiteSpace(dto.Content) ? null : dto.Content.Trim();
        existing.RecordDate = ParseDate(dto.Date);
        existing.PhotosJson = SerializePhotos(dto.Photos);
        existing.BabyId = babyId;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var existing = await _db.Milestones.FirstOrDefaultAsync(m => m.Id == id && m.UserId == uid, ct);
        if (existing is null) return false;
        existing.Deleted = true;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>从请求头 X-Baby-Id 或查询参数 babyId 解析宝宝 ID。</summary>
    private long? ResolveBabyId()
    {
        var ctx = _httpCtx.HttpContext;
        if (ctx is null) return null;
        if (ctx.Request.Headers.TryGetValue("X-Baby-Id", out var h) && long.TryParse(h, out var id)) return id;
        if (long.TryParse(ctx.Request.Query["babyId"], out var q)) return q;
        return null;
    }

    private static MilestoneRecordDto ToDto(Milestone m) => new()
    {
        Id = m.Id,
        Title = m.Title,
        Content = m.Content,
        Date = m.RecordDate.ToString("yyyy-MM-dd"),
        Photos = DeserializePhotos(m.PhotosJson),
    };

    private static DateTime ParseDate(string date)
    {
        return DateTime.TryParse(date, out var d)
            ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc)
            : DateTime.UtcNow.Date;
    }

    private static string SerializePhotos(List<string> photos)
    {
        if (photos is null || photos.Count == 0) return "[]";
        return JsonSerializer.Serialize(photos);
    }

    private static List<string> DeserializePhotos(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }
}
