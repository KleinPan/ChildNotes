using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChildNotes.Infrastructure.Data;

/// <summary>
/// 设计时 DbContext 工厂，供 dotnet ef migrations 命令使用。
/// 连接字符串在此仅为设计时占位，实际运行时由 Program.cs 注入。
/// </summary>
public class ChildNotesDbContextFactory : IDesignTimeDbContextFactory<ChildNotesDbContext>
{
    public ChildNotesDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ChildNotesDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=childnotes;Username=postgres;Password=postgres");
        return new ChildNotesDbContext(optionsBuilder.Options);
    }
}