namespace ChildNotes.Core.Config;

/// <summary>邮箱认证配置（SMTP + 验证码参数）。</summary>
public class EmailAuthOptions
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 465;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPass { get; set; } = string.Empty;
    public string FromName { get; set; } = "ChildNotes";
    public string FromAddress { get; set; } = string.Empty;

    public int CodeLength { get; set; } = 6;
    public int CodeTtlSeconds { get; set; } = 300; // 5 分钟
    public int MaxAttempts { get; set; } = 5;
    public int ResendIntervalSeconds { get; set; } = 60; // 同邮箱 60 秒限流

    public int AccessTokenExpireMinutes { get; set; } = 60; // 1 小时
    public int RefreshTokenExpireDays { get; set; } = 30; // 30 天

    /// <summary>
    /// Refresh Token 宽限期（秒）：旧 token 被撤销后，该时间窗口内重放仍可换取新 token。
    /// Rotation 场景下客户端可能因网络超时、并发重试、进程中断未保存新 token，
    /// 而旧 token 已被服务端撤销——若无宽限期，客户端下次 refresh 必收 401，
    /// 触发软登出导致永久掉线。宽限期内重放视为合法重试，直接再签发一对新 token。
    /// </summary>
    public int RefreshGracePeriodSeconds { get; set; } = 120;
}
