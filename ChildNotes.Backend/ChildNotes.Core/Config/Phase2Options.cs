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

    /// <summary>
    /// 单端点调用超时（秒），主用/备用端点共用。
    /// 必须小于 App 端 HTTP 30 秒超时：主用超时后降级备用端点，保证总耗时仍在 30 秒内返回。
    /// 端点超时视为端点故障（非用户取消），会触发 Fallback 降级。&lt;=0 时使用默认 20 秒。
    /// </summary>
    public int EndpointTimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// 备用 LLM 配置（可选）。主用调用失败（网络异常/非 2xx/超时）时自动降级到此项。
    /// 留空（或 ApiKey 为空）则禁用降级。Temperature/MaxTokens/ThinkingEnabled 等参数沿用主配置。
    /// </summary>
    public DeepSeekFallbackOptions? Fallback { get; set; }
}

/// <summary>备用 LLM 端点配置（仅端点级参数，生成参数沿用主配置）。</summary>
public class DeepSeekFallbackOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
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
    /// <summary>新用户注册自动赠送积分。</summary>
    public const int NewUserBonusPoints = 100;
    /// <summary>AI 喂养分析默认消耗积分（可被 AiCostOptions 覆盖）。</summary>
    public const int AiAnalysisDefaultCost = 10;

    public static readonly IReadOnlyDictionary<int, int> SignInBonusRewards = new Dictionary<int, int>
    {
        { 3, 3 }, { 5, 5 }, { 7, 7 }, { 30, 30 },
    };

    public static int CalculateSignInReward(int cycleDay)
    {
        return SignInBonusRewards.TryGetValue(cycleDay, out var bonus) ? bonus : BaseSignInReward;
    }

    /// <summary>日常任务奖励积分（每日/每周重置，需手动领取）。</summary>
    public static readonly IReadOnlyDictionary<string, int> DailyTaskRewards = new Dictionary<string, int>
    {
        { "daily_record", 5 },   // 每日记录：记录一条宝宝数据
        { "daily_feed", 3 },      // 喂奶打卡：记录一次喂奶
        { "daily_diaper", 2 },    // 换尿布打卡：记录一次换尿布
        { "weekly_growth", 20 },  // 每周成长：记录一次身高体重
    };
}

/// <summary>
/// AI 功能积分消耗配置：支持从配置文件/环境变量动态调整，前端通过 API 实时获取。
/// </summary>
public class AiCostOptions
{
    /// <summary>喂养分析单次消耗积分。</summary>
    public int AnalysisCost { get; set; } = PointsConstants.AiAnalysisDefaultCost;
}
