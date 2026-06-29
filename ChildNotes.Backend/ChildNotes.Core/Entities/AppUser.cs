using ChildNotes.Shared.Entities;

namespace ChildNotes.Core.Entities;

public class AppUser : AppUserBase, IAuditable
{
    public long? ReferrerUserId { get; set; }
    public DateTime? ReferrerBoundAt { get; set; }
}
