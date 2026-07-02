using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Data;

/// <summary>
/// DbContext 事务扩展：在支持事务的 provider（如 Npgsql）上启用事务包裹，
/// 在 InMemory 等不支持事务的 provider 上降级为直接执行，保证测试与生产环境一致的事务语义。
/// </summary>
public static class DbContextTransactionExtensions
{
    /// <summary>
    /// 在事务中执行 action。若 provider 不支持事务（InMemory），直接执行 action。
    /// 所有 SaveChanges 由 action 内部自行调用；本方法只负责事务边界。
    /// </summary>
    public static async Task ExecuteInTransactionAsync(
        this DbContext db,
        Func<Task> action,
        CancellationToken ct = default)
    {
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            await action();
            return;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await action();
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
