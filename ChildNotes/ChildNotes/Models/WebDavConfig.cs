namespace ChildNotes.Models;

/// <summary>
/// WebDAV 服务器配置（单行记录，id=1）。
/// </summary>
public sealed class WebDavConfig
{
    public int Id { get; set; } = 1;
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string RemotePath { get; set; } = "/ChildNotes/";
    public bool Enabled { get; set; }
    public bool AutoSync { get; set; } = true;
    public DateTime? LastSyncAt { get; set; }
    /// <summary>success / failed / partial</summary>
    public string? LastSyncStatus { get; set; }
    public DateTime UpdatedAt { get; set; }
}
