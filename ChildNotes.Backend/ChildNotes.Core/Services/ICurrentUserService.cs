namespace ChildNotes.Core.Services;

public interface ICurrentUserService
{
    string? UserId { get; }
    bool IsAuthenticated { get; }
    string RequireUserId();
}
