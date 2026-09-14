using ChildNotes.Core.Config;
using ChildNotes.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ChildNotes.Infrastructure.External;

/// <summary>
/// 基于 MailKit 的邮件发送实现。
/// 当前使用 QQ 邮箱 SMTP + 授权码。
/// 后续可替换为其他邮件服务（只需实现 IEmailSender）。
/// </summary>
public class MailKitEmailSender : IEmailSender
{
    private readonly EmailAuthOptions _opt;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(IOptions<EmailAuthOptions> opt, ILogger<MailKitEmailSender> logger)
    {
        _opt = opt.Value;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_opt.FromName, _opt.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new MailKit.Net.Smtp.SmtpClient();
        await client.ConnectAsync(_opt.SmtpHost, _opt.SmtpPort, true, ct);
        await client.AuthenticateAsync(_opt.SmtpUser, _opt.SmtpPass, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);

        _logger.LogInformation("验证码邮件已发送至 {Email}", MaskEmail(to));
    }

    /// <summary>邮箱脱敏：保留本地部分前 2 字符 + *** + @域名，PII 明文不落日志。</summary>
    private static string MaskEmail(string email)
    {
        var at = email.LastIndexOf('@');
        if (at <= 0) return "***"; // 非法格式兜底：完全不泄露
        var keep = Math.Min(2, at); // 本地部分不足 2 字符时保留全部
        return string.Concat(email.AsSpan(0, keep), "***", email.AsSpan(at));
    }
}
