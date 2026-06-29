namespace ChildNotes.Core.Services;

public interface ICurrentUserService
{
    long? UserId { get; }
    bool IsAuthenticated { get; }
    long RequireUserId();
}
