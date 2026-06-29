namespace ChildNotes.Core.Entities;

public class UserPoints : IAuditable
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public int Points { get; set; }
    public int TotalEarned { get; set; }
    public int TotalSpent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
