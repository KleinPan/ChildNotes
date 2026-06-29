using System.IO;
using System.Text.Json;

namespace ChildNotes.Services;

/// <summary>
/// AI 状态提示文案配置（对齐小程序版 dailyTips 数据源）。
/// 默认值与小程序后端 RecordServiceImpl.DAILY_TIPS 保持一致，
/// 同时支持从应用数据目录的 daily-tips.json 覆盖配置，实现动态可配置。
/// </summary>
public sealed class DailyTipsConfig
{
    /// <summary>良好状态下轮播的通用提示池（对应小程序 DAILY_TIPS 数组）。</summary>
    public List<string> DailyTips { get; set; } = new();

    /// <summary>发热状态提示。</summary>
    public string FeverTip { get; set; } = string.Empty;

    /// <summary>腹泻状态提示。</summary>
    public string DiarrheaTip { get; set; } = string.Empty;

    /// <summary>无宝宝/无统计数据时的兜底提示。</summary>
    public string DefaultTip { get; set; } = string.Empty;

    /// <summary>
    /// 状态卡片标题模板，使用 {0} 占位符代表宝宝姓名。
    /// 对齐小程序 good-status/index.wxml 第 10 行 babyName + '状态良好' 的拼接方式。
    /// </summary>
    public string GoodTitleTemplate { get; set; } = string.Empty;

    /// <summary>发热状态标题模板，{0} 代表宝宝姓名。</summary>
    public string FeverTitleTemplate { get; set; } = string.Empty;

    /// <summary>腹泻状态标题模板，{0} 代表宝宝姓名。</summary>
    public string DiarrheaTitleTemplate { get; set; } = string.Empty;

    /// <summary>未记录状态标题模板，{0} 代表宝宝姓名。</summary>
    public string NoRecordTitleTemplate { get; set; } = string.Empty;

    /// <summary>无宝宝时的标题（不涉及姓名占位符）。</summary>
    public string NoBabyTitle { get; set; } = string.Empty;
}

/// <summary>
/// 每日提示目录：负责加载与缓存 <see cref="DailyTipsConfig"/>。
/// 优先读取应用数据目录下的 daily-tips.json；不存在或解析失败时回退到默认值。
/// </summary>
public static class DailyTipsCatalog
{
    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChildNotes");

    /// <summary>配置文件路径（位于应用 LocalApplicationData/ChildNotes 目录）。</summary>
    public static string ConfigFilePath { get; } = Path.Combine(AppDir, "daily-tips.json");

    private static readonly DailyTipsConfig Default = new()
    {
        // 与小程序后端 RecordServiceImpl.DAILY_TIPS（第 95-108 行）保持一致
        DailyTips = new List<string>
        {
            "母乳喂养的宝宝每天大便2-5次是正常的哦",
            "宝宝6个月前纯母乳不需要额外喂水",
            "每天给宝宝做抚触，有助于神经系统发育",
            "宝宝的睡眠周期约45分钟，浅睡时别急着抱",
            "维生素D从出生15天开始补充，每天400IU",
            "宝宝的胃容量很小，按需喂养不必焦虑奶量",
            "拍嗝时竖抱15-20分钟，能有效减少溢奶",
            "宝宝哭闹不一定是饿了，先检查尿布和体温",
            "每天让宝宝趴一会儿，锻炼颈部背部力量",
            "母乳妈妈记得补充钙和铁，保证奶水营养",
            "洗澡水温37-38℃最合适，用手肘试温",
            "宝宝的体温在36.5-37.5℃之间都是正常的",
        },
        FeverTip = "多喂温水，物理降温，持续发热请及时就医",
        DiarrheaTip = "注意补充水分和电解质，清淡饮食为主",
        DefaultTip = "洗澡水温37-38℃最合适，用手肘试温",
        // 标题模板对齐小程序 good-status/index.wxml 第 10 行 babyName + '状态良好' 的拼接方式
        GoodTitleTemplate = "{0}状态良好",
        FeverTitleTemplate = "{0}体温偏高",
        DiarrheaTitleTemplate = "{0}肠胃需呵护",
        NoRecordTitleTemplate = "{0}今天还没记录",
        NoBabyTitle = "未添加宝宝",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>当前生效的配置（启动时加载，可调用 <see cref="Reload"/> 刷新）。</summary>
    public static DailyTipsConfig Current { get; private set; } = Load(ConfigFilePath);

    /// <summary>
    /// 从默认路径加载配置：优先读取 daily-tips.json，不存在或解析失败时回退到默认值。
    /// </summary>
    public static DailyTipsConfig Load() => Load(ConfigFilePath);

    /// <summary>
    /// 从指定路径加载配置。文件不存在或解析失败时回退到默认值；
    /// 文件中缺失的字段用默认值填充，确保配置完整可用。
    /// </summary>
    /// <param name="path">配置文件绝对路径。</param>
    public static DailyTipsConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return Clone(Default);

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<DailyTipsConfig>(json, JsonOptions);
            if (cfg is null) return Clone(Default);

            // 合并默认值：空字段用默认值填充，保证配置完整性
            if (cfg.DailyTips is null || cfg.DailyTips.Count == 0)
                cfg.DailyTips = new List<string>(Default.DailyTips);
            if (string.IsNullOrWhiteSpace(cfg.FeverTip)) cfg.FeverTip = Default.FeverTip;
            if (string.IsNullOrWhiteSpace(cfg.DiarrheaTip)) cfg.DiarrheaTip = Default.DiarrheaTip;
            if (string.IsNullOrWhiteSpace(cfg.DefaultTip)) cfg.DefaultTip = Default.DefaultTip;
            if (string.IsNullOrWhiteSpace(cfg.GoodTitleTemplate)) cfg.GoodTitleTemplate = Default.GoodTitleTemplate;
            if (string.IsNullOrWhiteSpace(cfg.FeverTitleTemplate)) cfg.FeverTitleTemplate = Default.FeverTitleTemplate;
            if (string.IsNullOrWhiteSpace(cfg.DiarrheaTitleTemplate)) cfg.DiarrheaTitleTemplate = Default.DiarrheaTitleTemplate;
            if (string.IsNullOrWhiteSpace(cfg.NoRecordTitleTemplate)) cfg.NoRecordTitleTemplate = Default.NoRecordTitleTemplate;
            if (string.IsNullOrWhiteSpace(cfg.NoBabyTitle)) cfg.NoBabyTitle = Default.NoBabyTitle;
            return cfg;
        }
        catch
        {
            // 配置文件损坏或无读写权限时，安全回退到默认值
            return Clone(Default);
        }
    }

    /// <summary>重新加载配置（运行时修改配置文件后调用）。</summary>
    public static void Reload() => Current = Load(ConfigFilePath);

    /// <summary>深拷贝配置，避免外部修改污染默认值。</summary>
    private static DailyTipsConfig Clone(DailyTipsConfig src) => new()
    {
        DailyTips = new List<string>(src.DailyTips),
        FeverTip = src.FeverTip,
        DiarrheaTip = src.DiarrheaTip,
        DefaultTip = src.DefaultTip,
        GoodTitleTemplate = src.GoodTitleTemplate,
        FeverTitleTemplate = src.FeverTitleTemplate,
        DiarrheaTitleTemplate = src.DiarrheaTitleTemplate,
        NoRecordTitleTemplate = src.NoRecordTitleTemplate,
        NoBabyTitle = src.NoBabyTitle,
    };
}
