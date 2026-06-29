using ChildNotes.Core.Constants;

namespace ChildNotes.Core.Entities;

public class BabyMember : IAuditable
{
    public long Id { get; set; }
    public long BabyId { get; set; }
    public long UserId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public string Status { get; set; } = StatusConstants.BabyMember.Active;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
