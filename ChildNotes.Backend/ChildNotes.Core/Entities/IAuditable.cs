namespace ChildNotes.Core.Entities;

/// <summary>具备审计时间戳的实体标记接口，配合 <c>AuditableSaveChangesInterceptor</c> 自动维护。</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}

/// <summary>仅含 CreatedAt 的实体（如 SignInRecord）。</summary>
public interface ICreatedAuditable
{
    DateTime CreatedAt { get; set; }
}
