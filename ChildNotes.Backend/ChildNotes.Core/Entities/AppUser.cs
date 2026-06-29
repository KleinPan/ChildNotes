namespace ChildNotes.Core.Entities;

public class AppUser : IAuditable
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string NickName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public int Gender { get; set; }
    public long? ReferrerUserId { get; set; }
    public DateTime? ReferrerBoundAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
