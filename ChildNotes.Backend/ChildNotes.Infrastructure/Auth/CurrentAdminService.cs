using ChildNotes.Core.Entities;

namespace ChildNotes.Infrastructure.Auth;

/// <summary>
/// 当前管理员上下文：Scoped 服务，承载 AdminAuthMiddleware 解析出的管理员。
/// 对齐 Java AdminAuthContext (ThreadLocal)。
/// </summary>
public interface ICurrentAdminService
{
    AdminAccount? Admin { get; }
    string RequireAdminId();
    void SetAdmin(AdminAccount admin);
}

public class CurrentAdminService : ICurrentAdminService
{
    private AdminAccount? _admin;
    public AdminAccount? Admin => _admin;

    public string RequireAdminId()
    {
        if (_admin is null) throw new Core.Exceptions.UnauthorizedException("Admin login is required");
        return _admin.Id;
    }

    public void SetAdmin(AdminAccount admin) => _admin = admin;
}
