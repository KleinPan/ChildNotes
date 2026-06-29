namespace ChildNotes.Core.Config;

public class AdminOptions
{
    public string InitUsername { get; set; } = "admin";
    public string InitPassword { get; set; } = string.Empty;
    public string InitDisplayName { get; set; } = "Administrator";
    public int TokenExpireHours { get; set; } = 12;
}
