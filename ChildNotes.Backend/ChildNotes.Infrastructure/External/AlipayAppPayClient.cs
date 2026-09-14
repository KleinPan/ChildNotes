using System.Security.Cryptography;
using System.Text;
using ChildNotes.Core.Config;
using ChildNotes.Shared.Services;

namespace ChildNotes.Infrastructure.External;

/// <summary>
/// 支付宝签名工具：RSA2 签名与验签。
/// 不引入 AlipaySDK.NET，自行实现最小依赖的签名逻辑。
/// 签名算法：SHA256withRSA（RSA2）。
/// 文档：https://opendocs.alipay.com/common/02kf5q
/// </summary>
public static class AlipaySignature
{
    // RSA 密钥参数缓存：解析 PEM（ImportFromPem + Base64 解码）每次调用重复执行开销不小，
    // 密钥配置不变可安全缓存。RSA 实例本身非线程安全，故缓存 RSAParameters，
    // 每次签名/验签时 RSA.Create()+ImportParameters（微秒级）。
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, RSAParameters> _keyCache = new();

    /// <summary>
    /// 对待签名内容进行 RSA2 签名。
    /// </summary>
    /// <param name="data">待签名内容（已按字典序拼接的 key=value&amp;key=value 格式，不 encode）。</param>
    /// <param name="privateKeyPem">应用私钥（PKCS8 PEM 格式，含或不含 header 均可）。</param>
    /// <returns>Base64 编码的签名值。</returns>
    public static string Sign(string data, string privateKeyPem)
    {
        using var rsa = CreateWithKey(privateKeyPem, isPrivate: true);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var signatureBytes = rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signatureBytes);
    }

    /// <summary>
    /// 验证支付宝回调通知的签名。
    /// </summary>
    /// <param name="data">待验签内容（已按字典序拼接）。</param>
    /// <param name="sign">支付宝返回的签名（Base64）。</param>
    /// <param name="alipayPublicKeyPem">支付宝公钥（PEM 格式）。</param>
    public static bool Verify(string data, string sign, string alipayPublicKeyPem)
    {
        try
        {
            using var rsa = CreateWithKey(alipayPublicKeyPem, isPrivate: false);
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var signBytes = Convert.FromBase64String(sign);
            return rsa.VerifyData(dataBytes, signBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>从缓存取密钥参数并创建独立 RSA 实例（首次解析 PEM 后缓存）。</summary>
    private static RSA CreateWithKey(string pem, bool isPrivate)
    {
        var parameters = _keyCache.GetOrAdd(pem, static (key, priv) =>
            priv ? LoadPrivateKey(key).ExportParameters(true) : LoadPublicKey(key).ExportParameters(false),
            isPrivate);
        var rsa = RSA.Create();
        rsa.ImportParameters(parameters);
        return rsa;
    }


    /// <summary>
    /// 将参数字典按 key 字典序升序拼接为 key1=value1&amp;key2=value2 格式。
    /// 值不进行 URL encode（支付宝签名时用原始值）。
    /// 过滤掉 null、空字符串和 sign 字段。
    /// </summary>
    public static string BuildSignContent(IDictionary<string, string> parameters)
    {
        var sorted = parameters
            .Where(kv => !string.IsNullOrEmpty(kv.Value) && kv.Key != "sign" && kv.Key != "sign_type")
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}");
        return string.Join("&", sorted);
    }

    /// <summary>
    /// 加载 PKCS8 格式的 RSA 私钥。
    /// 支持 PEM 格式（含 header）和纯 Base64 格式（不含 header）。
    /// </summary>
    private static RSA LoadPrivateKey(string privateKeyPem)
    {
        var rsa = RSA.Create();
        var pem = NormalizePem(privateKeyPem);

        // 尝试 PKCS8 加载
        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch { }

        // 尝试从纯 Base64 加载（PKCS8）
        var keyBytes = Convert.FromBase64String(StripPemHeaders(pem));
        rsa.ImportPkcs8PrivateKey(keyBytes, out _);
        return rsa;
    }

    /// <summary>
    /// 加载 RSA 公钥。
    /// 支持 PEM 格式和纯 Base64 格式。
    /// </summary>
    private static RSA LoadPublicKey(string publicKeyPem)
    {
        var rsa = RSA.Create();
        var pem = NormalizePem(publicKeyPem);

        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch { }

        // 纯 Base64 公钥（支付宝开放平台常见格式）
        var keyBytes = Convert.FromBase64String(StripPemHeaders(pem));
        rsa.ImportSubjectPublicKeyInfo(keyBytes, out _);
        return rsa;
    }

    private static string NormalizePem(string key)
    {
        var trimmed = key.Trim();
        if (trimmed.Contains("-----BEGIN")) return trimmed;

        // 不含 PEM header，补上 PKCS8 私钥 header
        return $"-----BEGIN PRIVATE KEY-----\n{trimmed}\n-----END PRIVATE KEY-----";
    }

    private static string StripPemHeaders(string pem)
    {
        return pem
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("-----BEGIN PUBLIC KEY-----", "")
            .Replace("-----END PUBLIC KEY-----", "")
            .Replace("-----BEGIN RSA PRIVATE KEY-----", "")
            .Replace("-----END RSA PRIVATE KEY-----", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim();
    }
}

/// <summary>
/// 支付宝 App 支付客户端：生成 App 支付所需的 orderInfo 字符串。
/// orderInfo 格式为 alipay.sdk.pay 的 biz_content 包装。
/// 文档：https://opendocs.alipay.com/open/204/105296/
/// </summary>
public class AlipayAppPayClient
{
    private readonly AlipayOptions _opt;

    public AlipayAppPayClient(AlipayOptions opt)
    {
        _opt = opt;
    }

    /// <summary>
    /// 生成 App 支付的 orderInfo 字符串（前端直接传给支付宝 SDK 的 PayTask）。
    /// 完整格式：公共参数 + biz_content + sign + sign_type
    /// </summary>
    /// <param name="orderNo">商户订单号</param>
    /// <param name="totalAmount">支付金额（元，如 "18.00"）</param>
    /// <param name="subject">订单标题</param>
    public string BuildOrderInfo(string orderNo, string totalAmount, string subject)
    {
        var bizContent = $$"""{"out_trade_no":"{{orderNo}}","total_amount":"{{totalAmount}}","subject":"{{subject}}","product_code":"{{_opt.ProductCode}}"}""";

        var publicParams = new Dictionary<string, string>
        {
            ["app_id"] = _opt.AppId,
            ["biz_content"] = bizContent,
            ["charset"] = "utf-8",
            ["method"] = "alipay.trade.app.pay",
            ["sign_type"] = _opt.SignType,
            // 支付宝开放平台要求 timestamp 为 GMT+8 时间；服务器时区为 UTC，须显式转北京时间
            ["timestamp"] = ChinaTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["version"] = "1.0",
        };

        var signContent = AlipaySignature.BuildSignContent(publicParams);
        var sign = AlipaySignature.Sign(signContent, _opt.PrivateKey);

        // 拼接最终 orderInfo：所有参数 URL encode 后用 & 连接
        var sb = new StringBuilder();
        foreach (var kv in publicParams)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kv.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kv.Value));
        }
        sb.Append("&sign=").Append(Uri.EscapeDataString(sign));
        return sb.ToString();
    }

    /// <summary>
    /// 验证支付宝异步通知签名。
    /// </summary>
    public bool VerifyNotifySign(IDictionary<string, string> parameters, string sign)
    {
        var signContent = AlipaySignature.BuildSignContent(parameters);
        return AlipaySignature.Verify(signContent, sign, _opt.AlipayPublicKey);
    }
}
