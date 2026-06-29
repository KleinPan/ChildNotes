using System.IO;
using System.Text.Json;
using ChildNotes.Services;

namespace ChildNotes.Tests;

/// <summary>
/// 验证 DailyTipsCatalog 的配置加载、默认值兜底与 JSON 覆盖逻辑。
/// 覆盖场景：默认值内容、文件缺失回退、JSON 解析失败回退、部分字段覆盖、空字段合并。
/// </summary>
public class DailyTipsCatalogTests : IDisposable
{
    private readonly string _tmpDir;

    public DailyTipsCatalogTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"cn_tips_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* 忽略清理失败 */ }
    }

    private string WriteConfig(string json)
    {
        var path = Path.Combine(_tmpDir, "daily-tips.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void DefaultConfig_AlignsWithMiniProgram()
    {
        // 验证默认提示池与小程序后端 RecordServiceImpl.DAILY_TIPS（12 条）一致
        var cfg = DailyTipsCatalog.Load(Path.Combine(_tmpDir, "not-exist.json"));

        Assert.Equal(12, cfg.DailyTips.Count);
        Assert.Contains("母乳喂养的宝宝每天大便2-5次是正常的哦", cfg.DailyTips);
        Assert.Contains("洗澡水温37-38℃最合适，用手肘试温", cfg.DailyTips);
        Assert.Contains("宝宝的体温在36.5-37.5℃之间都是正常的", cfg.DailyTips);

        // 验证异常状态提示默认值
        Assert.Equal("多喂温水，物理降温，持续发热请及时就医", cfg.FeverTip);
        Assert.Equal("注意补充水分和电解质，清淡饮食为主", cfg.DiarrheaTip);
        Assert.Equal("洗澡水温37-38℃最合适，用手肘试温", cfg.DefaultTip);

        // 验证标题模板默认值（对齐小程序 good-status babyName + '状态良好'）
        Assert.Equal("{0}状态良好", cfg.GoodTitleTemplate);
        Assert.Equal("{0}体温偏高", cfg.FeverTitleTemplate);
        Assert.Equal("{0}肠胃需呵护", cfg.DiarrheaTitleTemplate);
        Assert.Equal("{0}今天还没记录", cfg.NoRecordTitleTemplate);
        Assert.Equal("未添加宝宝", cfg.NoBabyTitle);
    }

    [Fact]
    public void Load_NonExistentFile_FallsBackToDefault()
    {
        // 文件不存在时应回退到默认值，不抛异常
        var path = Path.Combine(_tmpDir, "missing.json");
        var cfg = DailyTipsCatalog.Load(path);

        Assert.Equal(12, cfg.DailyTips.Count);
        Assert.False(string.IsNullOrEmpty(cfg.FeverTip));
        Assert.False(string.IsNullOrEmpty(cfg.DefaultTip));
    }

    [Fact]
    public void Load_MalformedJson_FallsBackToDefault()
    {
        // JSON 格式损坏时应安全回退
        var path = WriteConfig("{ this is not valid json ]");
        var cfg = DailyTipsCatalog.Load(path);

        Assert.Equal(12, cfg.DailyTips.Count);
    }

    [Fact]
    public void Load_ValidJson_OverridesAllFields()
    {
        // 完整配置覆盖
        var path = WriteConfig("""
        {
            "dailyTips": ["自定义提示1", "自定义提示2"],
            "feverTip": "自定义发热提示",
            "diarrheaTip": "自定义腹泻提示",
            "defaultTip": "自定义默认提示"
        }
        """);
        var cfg = DailyTipsCatalog.Load(path);

        Assert.Equal(2, cfg.DailyTips.Count);
        Assert.Equal("自定义提示1", cfg.DailyTips[0]);
        Assert.Equal("自定义提示2", cfg.DailyTips[1]);
        Assert.Equal("自定义发热提示", cfg.FeverTip);
        Assert.Equal("自定义腹泻提示", cfg.DiarrheaTip);
        Assert.Equal("自定义默认提示", cfg.DefaultTip);
    }

    [Fact]
    public void Load_PartialJson_MergesWithDefaults()
    {
        // 仅覆盖部分字段，其余字段应回退到默认值
        var path = WriteConfig("""
        {
            "feverTip": "仅覆盖发热提示",
            "goodTitleTemplate": "{0}很棒"
        }
        """);
        var cfg = DailyTipsCatalog.Load(path);

        Assert.Equal("仅覆盖发热提示", cfg.FeverTip);
        Assert.Equal("{0}很棒", cfg.GoodTitleTemplate);
        // 未覆盖字段保持默认值
        Assert.Equal(12, cfg.DailyTips.Count);
        Assert.False(string.IsNullOrEmpty(cfg.DiarrheaTip));
        Assert.False(string.IsNullOrEmpty(cfg.DefaultTip));
        Assert.Equal("{0}体温偏高", cfg.FeverTitleTemplate); // 未覆盖的标题模板保持默认
        Assert.Equal("未添加宝宝", cfg.NoBabyTitle);
    }

    [Fact]
    public void Load_EmptyDailyTipsArray_FallsBackToDefault()
    {
        // dailyTips 为空数组时应回退到默认池
        var path = WriteConfig("""
        {
            "dailyTips": [],
            "feverTip": "自定义发热"
        }
        """);
        var cfg = DailyTipsCatalog.Load(path);

        Assert.Equal(12, cfg.DailyTips.Count);
        Assert.Equal("自定义发热", cfg.FeverTip); // 自定义字段保留
    }

    [Fact]
    public void Load_WhitespaceStrings_FallBackToDefault()
    {
        // 仅含空白字符的字段应回退到默认值
        var path = WriteConfig("""
        {
            "feverTip": "   ",
            "defaultTip": ""
        }
        """);
        var cfg = DailyTipsCatalog.Load(path);

        Assert.Equal("多喂温水，物理降温，持续发热请及时就医", cfg.FeverTip);
        Assert.Equal("洗澡水温37-38℃最合适，用手肘试温", cfg.DefaultTip);
    }

    [Fact]
    public void Load_DoesNotMutateDefaultPool()
    {
        // 多次加载不应污染默认池（验证深拷贝）
        var path1 = WriteConfig("""
        {
            "dailyTips": ["第一份配置"]
        }
        """);
        // 改名以便第二次写入
        var path2 = Path.Combine(_tmpDir, "second.json");
        File.WriteAllText(path2, """
        {
            "dailyTips": ["第二份配置"]
        }
        """);

        var cfg1 = DailyTipsCatalog.Load(path1);
        var cfg2 = DailyTipsCatalog.Load(path2);

        Assert.Single(cfg1.DailyTips);
        Assert.Equal("第一份配置", cfg1.DailyTips[0]);
        Assert.Single(cfg2.DailyTips);
        Assert.Equal("第二份配置", cfg2.DailyTips[0]);

        // 默认池长度应保持 12
        var defaultCfg = DailyTipsCatalog.Load(Path.Combine(_tmpDir, "missing.json"));
        Assert.Equal(12, defaultCfg.DailyTips.Count);
    }

    [Fact]
    public void Current_IsInitializedAfterLoad()
    {
        // Current 静态属性应在首次访问时初始化（不抛异常）
        Assert.NotNull(DailyTipsCatalog.Current);
        Assert.NotEmpty(DailyTipsCatalog.Current.DailyTips);
    }

    [Fact]
    public void Load_TitleTemplates_CanBeOverridden()
    {
        // 验证标题模板可通过 JSON 完全覆盖
        var path = WriteConfig("""
        {
            "goodTitleTemplate": "{0}很健康",
            "feverTitleTemplate": "{0}发烧了",
            "diarrheaTitleTemplate": "{0}腹泻了",
            "noRecordTitleTemplate": "{0}今天还没记",
            "noBabyTitle": "请添加宝宝"
        }
        """);
        var cfg = DailyTipsCatalog.Load(path);

        Assert.Equal("{0}很健康", cfg.GoodTitleTemplate);
        Assert.Equal("{0}发烧了", cfg.FeverTitleTemplate);
        Assert.Equal("{0}腹泻了", cfg.DiarrheaTitleTemplate);
        Assert.Equal("{0}今天还没记", cfg.NoRecordTitleTemplate);
        Assert.Equal("请添加宝宝", cfg.NoBabyTitle);
    }

    [Fact]
    public void Load_TitleTemplates_DefaultWhenMissing()
    {
        // 完全没有标题模板字段的 JSON 应填充默认模板
        var path = WriteConfig("""
        {
            "dailyTips": ["仅提示池"]
        }
        """);
        var cfg = DailyTipsCatalog.Load(path);

        Assert.Equal("{0}状态良好", cfg.GoodTitleTemplate);
        Assert.Equal("{0}体温偏高", cfg.FeverTitleTemplate);
        Assert.Equal("{0}肠胃需呵护", cfg.DiarrheaTitleTemplate);
        Assert.Equal("{0}今天还没记录", cfg.NoRecordTitleTemplate);
        Assert.Equal("未添加宝宝", cfg.NoBabyTitle);
    }

    [Fact]
    public void Load_WhitespaceTitleTemplates_FallBackToDefault()
    {
        // 仅含空白字符的标题模板字段应回退到默认值
        var path = WriteConfig("""
        {
            "goodTitleTemplate": "  ",
            "noBabyTitle": ""
        }
        """);
        var cfg = DailyTipsCatalog.Load(path);

        Assert.Equal("{0}状态良好", cfg.GoodTitleTemplate);
        Assert.Equal("未添加宝宝", cfg.NoBabyTitle);
    }
}
