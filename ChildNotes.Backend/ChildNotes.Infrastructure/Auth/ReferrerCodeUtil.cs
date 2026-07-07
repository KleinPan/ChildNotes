using System.Security.Cryptography;
using System.Text;
using ChildNotes.Core.Services;

namespace ChildNotes.Infrastructure.Auth;

/// <summary>
/// 推荐码工具：HMAC-SHA256 签名 + Base64Url 编码。
/// userId 为 GUID 字符串（32 位无连字符）。
/// 格式：u_ + Base64Url(userId + ":" + HMAC_SHA256(userId) 完整 32 字节 hex)
/// </summary>
public class ReferrerCodeUtil : IReferrerCodeUtil
{
    private readonly byte[] _secret;

    public ReferrerCodeUtil(string secret)
    {
        _secret = Encoding.UTF8.GetBytes(secret);
    }

    public string Encode(string userId)
    {
        var idStr = userId ?? string.Empty;
        using var hmac = new HMACSHA256(_secret);
        var hashHex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(idStr)));
        var payload = $"{idStr}:{hashHex}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        return "u_" + Base64UrlEncode(bytes);
    }

    public string? Decode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        if (!code.StartsWith("u_")) return null;
        try
        {
            var payload = Encoding.UTF8.GetString(Base64UrlDecode(code[2..]));
            var idx = payload.IndexOf(':');
            if (idx <= 0) return null;
            var idStr = payload[..idx];
            var hashHex = payload[(idx + 1)..];
            if (string.IsNullOrEmpty(idStr)) return null;

            using var hmac = new HMACSHA256(_secret);
            var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(idStr)));
            // 固定时间比较，防时序攻击
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(hashHex))) return null;
            return idStr;
        }
        catch
        {
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded,
        };
        return Convert.FromBase64String(padded);
    }
}
