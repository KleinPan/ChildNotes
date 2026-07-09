using CommunityToolkit.Mvvm.Input;

namespace ChildNotes.ViewModels;

/// <summary>
/// "使用帮助"页 ViewModel：展示 Ai记 的核心功能、使用方法和注意事项。
/// 内容为静态说明，无业务逻辑，仅提供返回命令。
/// </summary>
public partial class HelpViewModel : ViewModelBase
{
    public HelpViewModel()
    {
        Title = "使用帮助";
    }

    /// <summary>帮助内容章节（供 UI 数据绑定展示）。</summary>
    public IReadOnlyList<HelpSection> Sections { get; } = new List<HelpSection>
    {
        new()
        {
            Icon = "✨",
            Title = "什么是 Ai记",
            Paragraphs = new List<string>
            {
                "Ai记 是 Ai记 的核心功能：通过自然语言一句话记录宝宝的日常喂养、睡眠、尿布、体温等信息。",
                "无需逐个点击表单，只需在首页底部输入框直接说话或打字，AI 会自动解析为结构化记录。",
            },
        },
        new()
        {
            Icon = "💬",
            Title = "如何使用",
            Paragraphs = new List<string>
            {
                "在首页底部输入框输入文字，点右侧发送按钮即可。",
                "支持一句话记录多条事件，例如：\"11点半睡到12点40，吃了130奶粉，喝10ml水\" 会自动拆分为睡眠、瓶喂、喝水三条记录。",
                "支持的时间表达：\"8:17\"、\"8点\"、\"8点半\"、\"晚上8点\"、\"下午3点半\"。",
                "AI 不可用时（未配置或网络异常）会自动降级为本地规则解析，覆盖最常见的喂奶/睡眠/尿布/体温等表述。",
            },
        },
        new()
        {
            Icon = "📝",
            Title = "支持的记录类型",
            Paragraphs = new List<string>
            {
                "· 喂奶：\"喝了120ml奶\"、\"亲喂左10右15分\"、\"吸出80ml\"",
                "· 睡眠：\"睡了30分钟\"、\"11点半睡到12点40\"（自动计算时长）",
                "· 尿布：\"换了尿布便便\"、\"尿布干爽\"、\"又尿又拉\"",
                "· 体温：\"体温38.5度\"、\"发烧37.8℃\"",
                "· 用药/营养：\"吃了半包保泰康颗粒\"、\"吃了1粒维D\"、\"喝了5滴伊可新\"",
                "· 身高体重：\"身高75cm 体重9.5kg\"",
            },
        },
        new()
        {
            Icon = "⚙️",
            Title = "AI 配置",
            Paragraphs = new List<string>
            {
                "进入「我的」→「AI 分析设置」可配置大模型。",
                "支持 OpenAI 兼容接口（DeepSeek、通义千问、本地 Ollama 等），填入 API 地址、密钥、模型名即可。",
                "可在「AI 分析设置」中点「测试连接」验证配置是否可用。",
                "解析来源可切换：local=本地 LLM 直接解析；server=后端解析接口（需配置同步地址）。",
            },
        },
        new()
        {
            Icon = "⚠️",
            Title = "注意事项",
            Paragraphs = new List<string>
            {
                "AI 解析为辅助功能，关键医疗/用药记录请人工核对后再保存。",
                "解析结果会保存到本地数据库并自动同步到后端（如已配置同步）。",
                "若解析失败或结果不符合预期，可手动通过底部 + 按钮选择对应记录类型填写。",
                "本地规则降级不支持所有复杂表述，建议复杂句子拆分多次输入以提升准确率。",
            },
        },
    };
}

/// <summary>帮助章节模型。</summary>
public sealed class HelpSection
{
    public string Icon { get; init; } = "";
    public string Title { get; init; } = "";
    public IReadOnlyList<string> Paragraphs { get; init; } = new List<string>();
}
