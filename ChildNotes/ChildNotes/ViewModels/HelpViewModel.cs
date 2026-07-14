using CommunityToolkit.Mvvm.Input;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// "使用帮助"页 ViewModel：展示 Ai记 的核心功能、使用方法和注意事项。
/// 内容为静态说明，无业务逻辑，仅提供返回命令。
/// </summary>
public partial class HelpViewModel : ViewModelBase
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    public HelpViewModel()
    {
        Title = _locale.GetString("Help_Title", "使用帮助");
        Sections = BuildSections();
        _locale.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>帮助内容章节（供 UI 数据绑定展示）。</summary>
    public IReadOnlyList<HelpSection> Sections { get; private set; } = new List<HelpSection>();

    private void OnLanguageChanged(AppLanguage lang)
    {
        Sections = BuildSections();
        OnPropertyChanged(nameof(Sections));
    }

    private List<HelpSection> BuildSections()
    {
        return new List<HelpSection>
        {
            new()
            {
                Icon = "✨",
                Title = _locale.GetString("Help_Section_AiNote", "什么是 Ai记"),
                Paragraphs = new List<string>
                {
                    _locale.GetString("Help_AiNote_Body1", "Ai记 是 Ai记 的核心功能：通过自然语言一句话记录宝宝的日常喂养、睡眠、尿布、体温等信息。"),
                    _locale.GetString("Help_AiNote_Body2", "无需逐个点击表单，只需在首页底部输入框直接说话或打字，AI 会自动解析为结构化记录。"),
                },
            },
            new()
            {
                Icon = "💬",
                Title = _locale.GetString("Help_Section_HowTo", "如何使用"),
                Paragraphs = new List<string>
                {
                    _locale.GetString("Help_HowTo_Body1", "在首页底部输入框输入文字，点右侧发送按钮即可。"),
                    _locale.GetString("Help_HowTo_Body2", "支持一句话记录多条事件，例如：\"11点半睡到12点40，吃了130奶粉，喝10ml水\" 会自动拆分为睡眠、瓶喂、喝水三条记录。"),
                    _locale.GetString("Help_HowTo_Body3", "支持的时间表达：\"8:17\"、\"8点\"、\"8点半\"、\"晚上8点\"、\"下午3点半\"。"),
                    _locale.GetString("Help_HowTo_Body4", "AI 不可用时（未配置或网络异常）会自动降级为本地规则解析，覆盖最常见的喂奶/睡眠/尿布/体温等表述。"),
                },
            },
            new()
            {
                Icon = "📝",
                Title = _locale.GetString("Help_Section_Types", "支持的记录类型"),
                Paragraphs = new List<string>
                {
                    _locale.GetString("Help_Types_1", "· 喂奶：\"喝了120ml奶\"、\"亲喂左10右15分\"、\"吸出80ml\""),
                    _locale.GetString("Help_Types_2", "· 睡眠：\"睡了30分钟\"、\"11点半睡到12点40\"（自动计算时长）"),
                    _locale.GetString("Help_Types_3", "· 尿布：\"换了尿布便便\"、\"尿布干爽\"、\"又尿又拉\""),
                    _locale.GetString("Help_Types_4", "· 体温：\"体温38.5度\"、\"发烧37.8℃\""),
                    _locale.GetString("Help_Types_5", "· 用药/营养：\"吃了半包保泰康颗粒\"、\"吃了1粒维D\"、\"喝了5滴伊可新\""),
                    _locale.GetString("Help_Types_6", "· 身高体重：\"身高75cm 体重9.5kg\""),
                },
            },
            new()
            {
                Icon = "⚙️",
                Title = _locale.GetString("Help_Section_Config", "AI 配置"),
                Paragraphs = new List<string>
                {
                    _locale.GetString("Help_Config_1", "进入「我的」→「AI 分析设置」可配置大模型。"),
                    _locale.GetString("Help_Config_2", "支持 OpenAI 兼容接口（DeepSeek、通义千问、本地 Ollama 等），填入 API 地址、密钥、模型名即可。"),
                    _locale.GetString("Help_Config_3", "可在「AI 分析设置」中点「测试连接」验证配置是否可用。"),
                    _locale.GetString("Help_Config_4", "解析来源可切换：local=本地 LLM 直接解析；server=后端解析接口（需配置同步地址）。"),
                },
            },
            new()
            {
                Icon = "⚠️",
                Title = _locale.GetString("Help_Section_Notes", "注意事项"),
                Paragraphs = new List<string>
                {
                    _locale.GetString("Help_Notes_1", "AI 解析为辅助功能，关键医疗/用药记录请人工核对后再保存。"),
                    _locale.GetString("Help_Notes_2", "解析结果会保存到本地数据库并自动同步到后端（如已配置同步）。"),
                    _locale.GetString("Help_Notes_3", "若解析失败或结果不符合预期，可手动通过底部 + 按钮选择对应记录类型填写。"),
                    _locale.GetString("Help_Notes_4", "本地规则降级不支持所有复杂表述，建议复杂句子拆分多次输入以提升准确率。"),
                },
            },
        };
    }
}

/// <summary>帮助章节模型。</summary>
public sealed class HelpSection
{
    public string Icon { get; init; } = "";
    public string Title { get; init; } = "";
    public IReadOnlyList<string> Paragraphs { get; init; } = new List<string>();
}
