namespace ChildNotes.Core.Config;

public class DeepSeekOptions
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "deepseek-chat";
    public double Temperature { get; set; } = 0.3;
    public int MaxTokens { get; set; } = 2500;
    public bool ThinkingEnabled { get; set; } = false;
    public string ReasoningEffort { get; set; } = "high";
}

public class OssOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string AccessKeySecret { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}

public class RateLimitOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxRequestsPerSecond { get; set; } = 5;
    public int BlacklistRequestsPerSecond { get; set; } = 10;
    public bool TrustProxyHeaders { get; set; } = true;
}

public class UploadOptions
{
    /// <summary>本地存储根目录（OSS 未配置时使用）</summary>
    public string LocalRoot { get; set; } = "uploads";
    /// <summary>访问本地文件的基础 URL，如 http://localhost:5000/uploads</summary>
    public string LocalBaseUrl { get; set; } = "/uploads";
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024; // 20MB
}

public static class PointsConstants
{
    public const int BaseSignInReward = 1;
    public const int SignInCycleDays = 30;
    public const int InviteRewardPoints = 100;

    public static readonly IReadOnlyDictionary<int, int> SignInBonusRewards = new Dictionary<int, int>
    {
        { 3, 3 }, { 5, 5 }, { 7, 7 }, { 30, 30 },
    };

    public static int CalculateSignInReward(int cycleDay)
    {
        return SignInBonusRewards.TryGetValue(cycleDay, out var bonus) ? bonus : BaseSignInReward;
    }
}
