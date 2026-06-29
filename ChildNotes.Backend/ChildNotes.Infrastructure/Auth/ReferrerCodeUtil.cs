using System.Security.Cryptography;
using System.Text;
using ChildNotes.Core.Services;

namespace ChildNotes.Infrastructure.Auth;

/// <summary>
/// 推荐码工具：HMAC-SHA256 签名 + Base64Url 编码，对齐 Java ReferrerCodeUtil。
/// 格式：u_ + Base64Url(userId + ":" + HMAC_SHA256(userId) 前12字节 hex)
/// 也支持纯数字 userId 直接解码（向后兼容）。
/// </summary>
public class ReferrerCodeUtil : IReferrerCodeUtil
{
    private readonly byte[] _secret;

    public ReferrerCodeUtil(string secret)
    {
        _secret = Encoding.UTF8.GetBytes(secret);
    }

    public string Encode(long userId)
    {
        var idStr = userId.ToString();
        using var hmac = new HMACSHA256(_secret);
        var hashHex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(idStr)));
        var payload = $"{idStr}:{hashHex}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        return "u_" + Base64UrlEncode(bytes);
    }

    public long? Decode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        // 纯数字直接解析
        if (long.TryParse(code, out var pureNum)) return pureNum;

        if (!code.StartsWith("u_")) return null;
        try
        {
            var payload = Encoding.UTF8.GetString(Base64UrlDecode(code[2..]));
            var idx = payload.IndexOf(':');
            if (idx <= 0) return null;
            var idStr = payload[..idx];
            var hashHex = payload[(idx + 1)..];
            if (!long.TryParse(idStr, out var uid)) return null;

            using var hmac = new HMACSHA256(_secret);
            var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(idStr)));
            // 固定时间比较，防时序攻击
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(hashHex))) return null;
            return uid;
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
