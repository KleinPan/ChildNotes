using ChildNotes.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ChildNotes.Infrastructure.Data;

/// <summary>
/// 审计时间戳拦截器：自动维护 <see cref="IAuditable"/> / <see cref="ICreatedAuditable"/> 实体的 CreatedAt/UpdatedAt。
/// 消除各 Service 中手动赋值 DateTime.UtcNow 的样板代码。
/// </summary>
public class AuditableSaveChangesInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        UpdateAuditableEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        UpdateAuditableEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private static void UpdateAuditableEntities(DbContext? context)
    {
        if (context is null) return;
        var now = DateTime.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity is IAuditable a)
                    {
                        if (a.CreatedAt == default) a.CreatedAt = now;
                        if (a.UpdatedAt == default) a.UpdatedAt = now;
                    }
                    else if (entry.Entity is ICreatedAuditable c)
                    {
                        if (c.CreatedAt == default) c.CreatedAt = now;
                    }
                    break;
                case EntityState.Modified:
                    if (entry.Entity is IAuditable aud)
                    {
                        // 避免覆盖显式设置的 UpdatedAt（如同步场景需保留远端时间戳）
                        if (!entry.Property(nameof(IAuditable.UpdatedAt)).IsModified)
                            aud.UpdatedAt = now;
                    }
                    break;
            }
        }
    }
}
