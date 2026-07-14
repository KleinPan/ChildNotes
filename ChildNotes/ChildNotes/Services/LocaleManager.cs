using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChildNotes.Services;

/// <summary>
/// 应用支持的语言。
/// </summary>
public enum AppLanguage
{
    /// <summary>简体中文（默认）</summary>
    ZhHans,
    /// <summary>英语</summary>
    En,
}

/// <summary>
/// i18n 核心管理器（单例）。
///
/// 设计要点：
/// - 翻译存储在 ResourceDictionary 中，XAML 端用 {DynamicResource Key} 绑定，
///   切换语言时热替换 MergedDictionaries 中的语言字典，DynamicResource 绑定自动刷新，UI 无需重启。
/// - AOT / Trimming 友好：无反射，语言资源字典直接在 C# 中以 ResourceDictionary 子类定义
///   （编译期构造，避免运行时 XAML 解析）。
/// - 语言偏好持久化到 JSON 文件（与 DeveloperPreferences 模式一致）。
/// - C# 代码通过 <see cref="GetString"/> 获取翻译（如 ViewModel 的错误文案）。
/// </summary>
public partial class LocaleManager : ObservableObject
{
    private static LocaleManager? _instance;
    public static LocaleManager Instance => _instance ??= new();

    [ObservableProperty] private AppLanguage _currentLanguage = AppLanguage.ZhHans;

    /// <summary>语言切换时触发。订阅者可在此刷新 ViewModel 中依赖翻译的属性。</summary>
    public event Action<AppLanguage>? LanguageChanged;

    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChildNotes");

    private static readonly string ConfigPath = Path.Combine(AppDir, "language.json");

    private LocaleManager() { }

    /// <summary>
    /// 初始化：加载持久化的语言偏好并应用到 Application.Resources。
    /// 应在 App.OnFrameworkInitializationCompleted 早期调用（先于任何 ViewModel 创建）。
    /// </summary>
    public void Initialize()
    {
        var lang = Load();
        ApplyLanguage(lang);
    }

    /// <summary>切换语言：热替换资源字典 + 持久化 + 触发事件。</summary>
    public void SetLanguage(AppLanguage lang)
    {
        if (lang == CurrentLanguage) return;
        ApplyLanguage(lang);
        Save(lang);
        LanguageChanged?.Invoke(lang);
    }

    private void ApplyLanguage(AppLanguage lang)
    {
        CurrentLanguage = lang;

        if (Avalonia.Application.Current?.Resources is { } res)
        {
            // 移除旧的语言资源字典（通过 LanguageResourceDictionary 标记类型识别）
            for (int i = res.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                if (res.MergedDictionaries[i] is LanguageResourceDictionary)
                    res.MergedDictionaries.RemoveAt(i);
            }

            // 加入新的语言资源字典
            res.MergedDictionaries.Add(new LanguageResourceDictionary(lang));
        }
    }

    /// <summary>
    /// 获取当前语言的翻译文本（供 C# 代码使用）。
    /// 资源未找到时返回 <paramref name="fallback"/>。
    /// </summary>
    public string GetString(string key, string fallback = "")
    {
        if (Avalonia.Application.Current?.Resources.TryGetResource(key, null, out var val) == true
            && val is string s && !string.IsNullOrEmpty(s))
            return s;
        return fallback;
    }

    // ===== 持久化 =====

    private static AppLanguage Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return AppLanguage.ZhHans;
            var json = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize<LangConfig>(json);
            return cfg?.Language is "en" or "en-US" ? AppLanguage.En : AppLanguage.ZhHans;
        }
        catch { return AppLanguage.ZhHans; }
    }

    private static void Save(AppLanguage lang)
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            var cfg = new LangConfig { Language = lang == AppLanguage.En ? "en" : "zh-CN" };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg));
        }
        catch { /* 非致命：持久化失败不影响当前会话 */ }
    }

    private sealed class LangConfig { public string Language { get; set; } = "zh-CN"; }
}

/// <summary>
/// 按语言加载对应 ResourceDictionary 的包装类。
/// 继承 ResourceDictionary 便于在 MergedDictionaries 中通过类型识别并移除。
/// 翻译文本在 C# 中直接构造（AOT / Trimming 友好，无运行时 XAML 解析）。
/// </summary>
internal sealed class LanguageResourceDictionary : ResourceDictionary
{
    public LanguageResourceDictionary(AppLanguage lang)
    {
        if (lang == AppLanguage.En)
            FillEn();
        else
            FillZhHans();
    }

    // ===== 简体中文 =====
    private void FillZhHans()
    {
        // ===== MainShellView TabBar =====
        this["Tab_Home"] = "首页";
        this["Tab_Feeding"] = "喂养";
        this["Tab_Growth"] = "成长";
        this["Tab_Mine"] = "我的";

        // ===== Common =====
        this["Common_Ok"] = "确定";
        this["Common_Cancel"] = "取消";
        this["Common_Back"] = "返回";
        this["Common_Save"] = "保存";
        this["Common_Delete"] = "删除";
        this["Common_Confirm"] = "确认";
        this["Common_Copy"] = "复制";
        this["Common_Add"] = "添加";
        this["Common_Expand"] = "展开";
        this["Common_Collapse"] = "收起";
        this["Common_Loading"] = "加载中...";
        this["Common_Empty"] = "暂无数据";

        // ===== MineView =====
        this["Mine_Section_Baby"] = "宝宝";
        this["Mine_Section_Tools"] = "工具";
        this["Mine_Section_Settings"] = "设置";
        this["Mine_BabyManager"] = "宝宝管理";
        this["Mine_BabyCount_Suffix"] = "个宝宝";
        this["Mine_Family"] = "家人管理";
        this["Mine_Membership"] = "会员中心";
        this["Mine_AiAnalysis"] = "宝宝喂养分析";
        this["Mine_AiAnalysis_Sub"] = "最近一周";
        this["Mine_Statistics"] = "统计分析";
        this["Mine_Points"] = "积分任务";
        this["Mine_AiSettings"] = "AI 分析设置";
        this["Mine_ReminderSettings"] = "提醒设置";
        this["Mine_SyncSettings"] = "数据同步";
        this["Mine_DeveloperOptions"] = "开发者选项";
        this["Mine_PrivacyPolicy"] = "隐私政策";
        this["Mine_UserAgreement"] = "用户协议";
        this["Mine_InAppMessage"] = "应用消息";
        this["Mine_Help"] = "使用帮助";
        this["Mine_About"] = "关于";
        this["Mine_Logout"] = "退出登录";
        // MineViewModel
        this["Mine_Role_Parent"] = "家长";
        this["Mine_NotLoggedIn"] = "未登录";
        this["Mine_Membership_Active"] = "会员";
        this["Mine_Membership_Regular"] = "普通用户";

        // ===== LanguageSettingsView =====
        this["Language_Title"] = "语言";
        this["Language_ZhHans"] = "简体中文";
        this["Language_En"] = "English";
        this["Language_Description"] = "切换应用显示语言（重启后仍生效）";

        // ===== HomeView =====
        this["Home_Stats"] = "统计";
        this["Home_CheckIn"] = "签到";
        this["Home_LastFeed"] = "🍼 距上次喂奶";
        this["Home_Diaper"] = "💩 换尿布";
        this["Home_SleepToday"] = "😴 今日已睡";
        this["Home_SleepCount"] = "1次";
        this["Home_HeightWeight"] = "📏 身高体重";
        this["Home_AiChangeHint"] = "—— 宝宝的变化 ~";
        this["Home_VaccineTracking"] = "疫苗追踪";
        this["Home_QuickRecord"] = "补记";
        this["Home_AbnormalTracking"] = "异常 / 生病记";
        this["Home_Record"] = "记录";
        this["Home_ContinueRecord"] = "继续记录";
        this["Home_Recovered"] = "已恢复";
        // HomeCoreViewModel
        this["Home_DailyTip_Default"] = "记录宝宝的每一天，陪伴健康成长";
        this["Home_DailyTip_Fever"] = "宝宝正在发热，注意监测体温和补水";
        this["Home_DailyTip_Diarrhea"] = "宝宝有腹泻症状，注意观察和补水";
        this["Home_DailyTip_Active"] = "今天记录很用心，继续加油！";
        this["Home_DailyTip_Empty"] = "今天还没有记录，点击下方快捷操作开始吧";
        this["Home_NoBaby"] = "未添加宝宝";
        this["Home_Diaper_Zero"] = "0次";
        this["Home_DiaperDetail_Zero"] = "便0 尿0";
        this["Home_Sleep_Zero"] = "0小时0分钟";
        this["Home_Minutes"] = "分钟";
        this["Home_HoursMinutes"] = "{0}小时{1}分钟";
        this["Home_Days"] = "{0}天";
        this["Home_FeedCount"] = "{0}次 {1}ml";
        this["Home_FeedCountNoMl"] = "{0}次";
        this["Home_DiaperCount"] = "{0}次";
        this["Home_DiaperDetail"] = "便{0} 尿{1}";
        // AiStatusViewModel
        this["Home_Ai_GoodTitle"] = "{0}状态良好";
        this["Home_Ai_FeverTitle"] = "{0}体温偏高";
        this["Home_Ai_DiarrheaTitle"] = "{0}肠胃需呵护";
        this["Home_Ai_NoRecordTitle"] = "{0}今天还没记录";
        this["Home_Ai_NoBabyTitle"] = "未添加宝宝";
        this["Home_Ai_SubtitleGood"] = "正在快乐成长中~";
        this["Home_Ai_SubtitleFever"] = "当前体温{0}℃";
        this["Home_Ai_SubtitleDiarrhea"] = "今日有腹泻记录";
        this["Home_Ai_SubtitleGreat"] = "吃得好睡得香~";
        this["Home_Ai_SubtitleNoRecord"] = "点击下方快捷按钮开始吧";
        // AbnormalTrackingViewModel
        this["Home_Abnormal_Fever"] = "发烧";
        this["Home_Abnormal_Diarrhea"] = "腹泻";
        this["Home_Abnormal_Other"] = "其他异常";
        this["Home_Abnormal_Respiratory"] = "呼吸道：{0}";
        this["Home_Abnormal_Vomit"] = "呕吐";
        this["Home_Abnormal_Medicated"] = "已用药";
        this["Home_Abnormal_Summary"] = "今日有异常记录，请关注宝宝状态";

        // ===== FeedingView =====
        this["Feeding_GoToday"] = "回到今天";
        this["Feeding_StatsFeed"] = "喂奶{0}次 · {1}ml";
        this["Feeding_StatsFeedNoMl"] = "喂奶{0}次";
        this["Feeding_StatsBreast"] = "亲喂{0}次";
        this["Feeding_StatsDiaper"] = "{0}尿布 · 便{1} · 尿{2}";
        this["Feeding_StatsWater"] = "喝水{0}次 · {1}ml";
        this["Feeding_StatsSupplement"] = "补给{0}次";
        this["Feeding_StatsSleep"] = "睡眠{0}分钟";
        this["Feeding_RecordList"] = "记录列表";
        this["Feeding_FilterAll"] = "全部";
        this["Feeding_FilterFeed"] = "喂奶";
        this["Feeding_FilterSleep"] = "睡眠";
        this["Feeding_FilterDiaper"] = "尿布";
        this["Feeding_FilterActivity"] = "活动";
        this["Feeding_FilterOther"] = "其他";
        this["Feeding_NoRecords"] = "当天暂无记录";
        this["Feeding_DeleteConfirmTitle"] = "确认删除";
        this["Feeding_DeleteConfirmMsg"] = "确定要删除这条记录吗？";
        this["Feeding_Deleted"] = "已删除记录";
        // FeedingViewModel weekdays
        this["Weekday_Mon"] = "周一";
        this["Weekday_Tue"] = "周二";
        this["Weekday_Wed"] = "周三";
        this["Weekday_Thu"] = "周四";
        this["Weekday_Fri"] = "周五";
        this["Weekday_Sat"] = "周六";
        this["Weekday_Sun"] = "周日";
        // FeedingViewModel record display
        this["Rec_Feed_Breast"] = "母乳亲喂 {0}{1}";
        this["Rec_Feed_BreastLeft"] = "左";
        this["Rec_Feed_BreastRight"] = "右";
        this["Rec_Feed_Bottle"] = "瓶喂";
        this["Rec_Feed_BottleExpressed"] = "瓶喂(母乳)";
        this["Rec_Duration_Min"] = "{0}分钟";
        this["Rec_Diaper_Wet"] = "小便";
        this["Rec_Diaper_Dirty"] = "大便";
        this["Rec_Diaper_Both"] = "大小便";
        this["Rec_Diaper_Default"] = "换尿布";
        this["Rec_Sleep"] = "睡眠";
        this["Rec_SleepRange"] = "{0} → {1}";
        this["Rec_SleepStart"] = "{0} 开始";
        this["Rec_Duration_Long"] = "共 {0}小时{1}分钟";
        this["Rec_Duration_Short"] = "共 {0}分钟";
        this["Rec_Temperature"] = "体温";
        this["Rec_Growth"] = "成长记录";
        this["Rec_GrowthHeight"] = "身高{0}cm ";
        this["Rec_GrowthWeight"] = "体重{0}kg";
        this["Rec_Water"] = "喝水";
        this["Rec_WaterExtra"] = "饮水";
        this["Rec_Pump"] = "吸奶";
        this["Rec_Abnormal"] = "异常记录";
        this["Rec_Activity"] = "活动";
        this["Rec_Supplement_Medicine"] = "用药记录";
        this["Rec_Supplement_Supplement"] = "补充剂记录";
        this["Rec_SupplementExtra_Medicine"] = "用药";
        this["Rec_SupplementExtra_Supplement"] = "补充剂";
        this["Rec_DoseHalf"] = "半{0}";
        this["Rec_Complementary"] = "辅食";
        this["Rec_Abnormal_Fever"] = "发烧";
        this["Rec_Abnormal_Diarrhea"] = "腹泻";
        this["Rec_Abnormal_Vomit"] = "呕吐";
        this["Rec_Abnormal_Medicine"] = "用药";
        this["Rec_Abnormal_Respiratory"] = "呼吸道：{0}";
        this["Rec_Abnormal_Medicated"] = "已用药";
        this["Rec_NotBorn"] = "未出生";
        this["Rec_AgeBornDays"] = "出生{0}天";
        this["Rec_AgeMonths"] = "{0}个月{1}天";
        this["Rec_AgeYears"] = "{0}岁{1}个月{2}天";

        // ===== GrowthView =====
        this["Growth_Title"] = "成长记录";
        this["Growth_Subtitle"] = "记录宝宝每一个重要时刻";
        this["Growth_AddMoment"] = "记录成长时刻";
        this["Growth_EmptyTitle"] = "还没有成长记录";
        this["Growth_EmptyHint1"] = "点击上方「记录成长时刻」";
        this["Growth_EmptyHint2"] = "添加宝宝的第一个里程碑吧~";
        this["Growth_AddNow"] = "立即添加";
        this["Growth_EditDate"] = "日期";
        this["Growth_EditTitle"] = "标题";
        this["Growth_EditTitlePlaceholder"] = "如：第一次翻身、第一次叫妈妈...";
        this["Growth_EditContentLabel"] = "详细内容（选填）";
        this["Growth_EditContentPlaceholder"] = "记录这个美好时刻的细节...";
        this["Growth_EditPhotosLabel"] = "照片（选填，最多4张）";
        this["Growth_EditAddPhotoHint"] = "点击 + 添加";
        this["Growth_Uploading"] = "上传中";
        this["Growth_EditAdd"] = "编辑成长时刻";

        // ===== BabyManagerView =====
        this["BabyMgr_Title"] = "宝宝管理";
        this["BabyMgr_EmptyTitle"] = "还没有宝宝信息";
        this["BabyMgr_EmptyHint"] = "先添加宝宝后，就能在这里查看和修改。";
        this["BabyMgr_Current"] = "当前";
        this["BabyMgr_AddBaby"] = "+ 添加宝宝";
        this["BabyMgr_ChangeAvatar"] = "点击更换头像";
        this["BabyMgr_Boy"] = "👦 男宝";
        this["BabyMgr_Girl"] = "👧 女宝";
        this["BabyMgr_Name"] = "姓名";
        this["BabyMgr_NamePlaceholder"] = "请输入宝宝姓名或小名";
        this["BabyMgr_Birthday"] = "生日";
        this["BabyMgr_BirthdayPlaceholder"] = "请选择出生日期";
        this["BabyMgr_BabyId"] = "宝宝 ID";
        this["BabyMgr_DeleteBaby"] = "删除此宝宝";
        this["BabyMgr_DeleteTitle"] = "删除宝宝";
        this["BabyMgr_DeleteConfirm"] = "确定要删除「{0}」吗？相关记录将保留，但宝宝信息将无法恢复。";
        this["BabyMgr_AddTitle"] = "添加宝宝";
        this["BabyMgr_EditTitle"] = "编辑宝宝";
        this["BabyMgr_ErrName"] = "请输入宝宝姓名";
        this["BabyMgr_ErrBirthday"] = "请选择出生日期";
        this["BabyMgr_IdEmpty"] = "宝宝 ID 为空";
        this["BabyMgr_ClipUnavailable"] = "剪贴板不可用";
        this["BabyMgr_IdCopied"] = "宝宝 ID 已复制";
        this["BabyMgr_PickAvatarTitle"] = "选择头像";
        this["BabyMgr_PickImageFilter"] = "图片文件";

        // ===== BabySetupView =====
        this["BabySetup_Welcome"] = "欢迎使用成长记录";
        this["BabySetup_Hint"] = "请先添加宝宝信息，再开始记录吧";
        this["BabySetup_Gender"] = "性别";
        this["BabySetup_Name"] = "姓名";
        this["BabySetup_Birthday"] = "生日";
        this["BabySetup_Start"] = "开始记录";
        this["BabySetup_Later"] = "稍后再说";
        this["BabySetup_ErrName"] = "请输入宝宝姓名";
        this["BabySetup_ErrBirthday"] = "请选择出生日期";

        // ===== AiAnalysisView =====
        this["AiAnalysis_Title"] = "喂养分析";
        this["AiAnalysis_Subtitle"] = "宝宝喂养分析";
        this["AiAnalysis_PointsCost"] = "积分 / 消耗 ";
        this["AiAnalysis_CheckIn"] = "签到赚积分";
        this["AiAnalysis_RangeHint"] = "选择连续 7 天范围生成 AI 分析";
        this["AiAnalysis_RangeCost"] = "(每次消耗积分)";
        this["AiAnalysis_StartPlaceholder"] = "开始";
        this["AiAnalysis_To"] = "至";
        this["AiAnalysis_EndPlaceholder"] = "结束";
        this["AiAnalysis_Cancel"] = "取消分析";
        this["AiAnalysis_Records"] = "分析记录";
        this["AiAnalysis_LoadMore"] = "加载更多";
        this["AiAnalysis_AllLoaded"] = "— 已全部加载 —";
        this["AiAnalysis_Empty"] = "暂无分析记录";
        this["AiAnalysis_EmptyHint"] = "选择日期范围后点击上方按钮生成";
        this["AiAnalysis_GeneratedAt"] = "生成于 {0}";
        this["AiAnalysis_BackToList"] = "返回列表";
        // AiAnalysisViewModel
        this["AiAnalysis_RangeTipDefault"] = "请选择连续 7 天作为分析区间";
        this["AiAnalysis_GenerateNew"] = "生成新的分析";
        this["AiAnalysis_RangeTooShort"] = "分析区间不能少于 7 天";
        this["AiAnalysis_RangeTooLong"] = "分析区间不能超过 7 天";
        this["AiAnalysis_RangeOk"] = "将分析该连续 7 天内的记录";
        this["AiAnalysis_AlreadyAnalyzed"] = "该区间已分析";
        this["AiAnalysis_Analyzing"] = "正在分析...";
        this["AiAnalysis_ErrEnableAi"] = "请先在设置中启用大模型";
        this["AiAnalysis_ErrPointsShort"] = "积分不足，需 {0} 积分，当前 {1} 积分";
        this["AiAnalysis_ErrPointsShortFull"] = "积分不足，需 {0} 积分，当前 {1} 积分（每日签到可获取积分）";
        this["AiAnalysis_ErrPointsShortFinal"] = "积分不足，本次分析需 {0} 积分，当前余额 {1} 积分";
        this["AiAnalysis_Done"] = "分析完成";
        this["AiAnalysis_Canceled"] = "已取消分析";

        // ===== AiSettingsView =====
        this["AiSettings_Title"] = "AI 分析设置";
        this["AiSettings_EnableAi"] = "启用 AI 分析";
        this["AiSettings_EnableAiHint"] = "关闭后无法生成新的分析";
        this["AiSettings_LlmConfig"] = "大模型配置";
        this["AiSettings_ApiUrl"] = "API 地址";
        this["AiSettings_ModelName"] = "模型名称";
        this["AiSettings_ParseService"] = "Ai 记 解析服务";
        this["AiSettings_SourceLabel"] = "服务来源";
        this["AiSettings_SourceHint"] = "选择 Ai 记和宝宝喂养分析使用本地大模型还是后端服务";
        this["AiSettings_SourceLocal"] = "本地大模型";
        this["AiSettings_SourceServer"] = "后端服务";
        this["AiSettings_ServerUrl"] = "后端地址";
        this["AiSettings_Hint"] = "提示";
        this["AiSettings_HintContent"] = "后端地址需在同步设置中配置";
        this["AiSettings_GenParams"] = "生成参数";
        this["AiSettings_Temperature"] = "温度";
        this["AiSettings_MaxTokens"] = "最大 Token 数";
        this["AiSettings_Save"] = "保存配置";
        this["AiSettings_NotesTitle"] = "说明";
        this["AiSettings_Note1"] = "• 配置 OpenAI 兼容的大模型 API，支持 OpenAI、DeepSeek、通义千问、Moonshot 等";
        this["AiSettings_Note2"] = "• API 地址默认追加 /v1/chat/completions；若模型仅支持 v2 路径（如阿里云百炼 qwen3），请直接填写完整 URL，例如 https://dashscope.aliyuncs.com/api/ais-v2/chat/completions";
        this["AiSettings_Note3"] = "• 本地大模型（Ollama/vLLM/LM Studio 等）可不填 API Key";
        this["AiSettings_Note4"] = "• API Key 仅保存在本地，不会上传服务器";
        this["AiSettings_Note5"] = "• 温度越高输出越多样，越低越确定";
        this["AiSettings_Note6"] = "• 修改配置后需重新生成分析才会生效";

        // ===== ReminderSettingsView =====
        this["Reminder_Title"] = "提醒设置";
        this["Reminder_FeedSection"] = "喂奶提醒";
        this["Reminder_FeedEnable"] = "启用喂奶提醒";
        this["Reminder_FeedEnableHint"] = "记录喂奶后按间隔时长提醒下次喂奶";
        this["Reminder_FeedInterval"] = "提醒间隔";
        this["Reminder_FeedIntervalUnit"] = "{0} 小时";
        this["Reminder_FeedIntervalHint"] = "新生儿建议 2-3 小时，大月龄可适当延长";
        this["Reminder_SleepSection"] = "睡眠提醒";
        this["Reminder_SleepEnable"] = "启用睡眠提醒";
        this["Reminder_SleepEnableHint"] = "宝宝睡满阈值时长后提醒是否需要唤醒";
        this["Reminder_SleepThreshold"] = "提醒阈值";
        this["Reminder_SleepThresholdHint"] = "新生儿睡眠周期短，大月龄可能睡整觉";
        this["Reminder_Save"] = "保存配置";
        this["Reminder_NotesTitle"] = "说明";
        this["Reminder_Note1"] = "• 开关切换立即生效，时长修改需点保存按钮";
        this["Reminder_Note2"] = "• 修改后对后续记录的喂奶/睡眠立即生效；已调度的旧提醒不会自动更新";
        this["Reminder_Note3"] = "• App 被杀进程后仍能准点触发（由系统 AlarmManager 托管）";
        this["Reminder_Note4"] = "• Android 13+ 需授予通知权限，可在系统设置中确认";

        // ===== SyncSettingsView =====
        this["Sync_Title"] = "数据同步";
        this["Sync_SyncLabel"] = "同步";
        this["Sync_EnableCloud"] = "启用云同步";
        this["Sync_EnableCloudHint"] = "关闭时本地数据仍可正常使用";
        this["Sync_Server"] = "服务器";
        this["Sync_AddressLabel"] = "地址";
        this["Sync_CurrentEffect"] = "当前生效";
        this["Sync_NetworkStatus"] = "网络状态";
        this["Sync_SyncLog"] = "同步日志";
        this["Sync_NoLog"] = "暂无同步记录";
        this["Sync_NotesTitle"] = "说明";
        this["Sync_Note1"] = "• 本地数据为优先源，离线可正常使用";
        this["Sync_Note2"] = "• 服务器地址与登录账号由应用自动配置，无需手动填写";
        this["Sync_Note3"] = "• 网络恢复后自动同步；新增记录 5 秒后自动同步";
        this["Sync_Note4"] = "• 同步失败会自动重试，瞬时错误无需手动干预";
        this["Sync_Note5"] = "• 冲突按更新时间合并（后写覆盖）";
        // SyncSettingsView code-behind
        this["Sync_StatusSuccess"] = "成功";
        this["Sync_StatusFailed"] = "失败";
        this["Sync_StatusRunning"] = "进行中";

        // ===== StatisticsView =====
        this["Stats_Title"] = "统计分析";
        this["Stats_To"] = "至";
        this["Stats_Total"] = "合计";
        this["Stats_Peak"] = "峰值";
        this["Stats_Trend"] = "趋势";
        this["Stats_Detail"] = "明细";

        // ===== PointsView =====
        this["Points_Title"] = "积分任务";
        this["Points_YourPoints"] = "您的积分";
        this["Points_Unit"] = "分";
        this["Points_Summary"] = "累计获得 {0} 分 · 已使用 {1} 分";
        this["Points_Streak"] = "连续签到";
        this["Points_StreakDays"] = "您已连续签到 {0} 天";
        this["Points_TasksEarn"] = "任务赚积分";

        // ===== MembershipView =====
        this["Membership_Title"] = "会员中心";
        this["Membership_AiToday"] = "AI 记（今日）";
        this["Membership_AiWeek"] = "AI 分析（本周）";
        this["Membership_ExpireAt"] = "会员到期：{0}";
        this["Membership_Benefits"] = "会员权益";
        this["Membership_Benefit1Title"] = "AI 记次数提升";
        this["Membership_Benefit1Desc"] = "普通用户 10 次/天，会员 100 次/天";
        this["Membership_Benefit2Title"] = "AI 分析次数提升";
        this["Membership_Benefit2Desc"] = "普通用户 1 次/周，会员 10 次/周";
        this["Membership_Benefit3Title"] = "抽奖积分折扣";
        this["Membership_Benefit3Desc"] = "会员抽奖消耗积分享 8 折优惠";
        this["Membership_Benefit4Title"] = "成长记录图片高清同步";
        this["Membership_Benefit4Desc"] = "普通用户 1280px/85% 质量，会员 1920px/92% 接近无损";
        this["Membership_SelectPlan"] = "选择套餐";
        this["Membership_Recommended"] = "推荐";
        this["Membership_Selected"] = "已选：";
        this["Membership_Subscribe"] = "立即开通";
        // MembershipView code-behind
        this["Membership_StatusMember"] = "会员用户";
        this["Membership_StatusRegular"] = "普通用户";
        this["Membership_PlanMonthly"] = "月卡";
        this["Membership_PlanQuarterly"] = "季卡";
        this["Membership_PlanYearly"] = "年卡";
        this["Membership_PlanNone"] = "未选择";
        // MembershipViewModel
        this["Membership_ErrCreateOrder"] = "创建订单失败，请检查网络";
        this["Membership_PaySuccessMock"] = "支付成功（Mock 模式）";
        this["Membership_PayFailed"] = "支付失败：{0}";
        this["Membership_SubscribeOk"] = "会员开通成功！";
        this["Membership_OrderClosed"] = "订单已关闭";
        this["Membership_PayConfirming"] = "支付结果确认中，请稍后查看会员状态";

        // ===== FamilyView =====
        this["Family_Title"] = "家人管理";
        this["Family_Refresh"] = "刷新";
        this["Family_JoinById"] = "+ 通过宝宝 ID 加入家庭";
        this["Family_MyRole"] = "我的角色";
        this["Family_BabyId"] = "宝宝 ID";
        this["Family_Me"] = "（我）";
        this["Family_Owner"] = "主人";
        this["Family_JoinFamily"] = "加入家庭";
        this["Family_JoinPlaceholder"] = "向宝宝主人索取 ID";
        this["Family_SelectRole"] = "选择身份";
        this["Family_JoinNow"] = "确定加入";
        this["Family_RoleEditorTitle"] = "我的角色 · {0}";
        // FamilyViewModel
        this["Family_Loading"] = "加载中…";
        this["Family_EmptyNoServer"] = "无法连接服务器，请先在『数据同步』中配置并启用";
        this["Family_EmptyNoFamily"] = "还没有加入任何家庭";
        this["Family_EmptyError"] = "加载失败：{0}";
        this["Family_EmptyNotLoaded"] = "尚未加载";
        this["Family_Refreshed"] = "已刷新";
        this["Family_ErrIdEmpty"] = "请输入宝宝 ID";
        this["Family_ErrJoinFailed"] = "加入失败，请检查宝宝 ID 或网络";
        this["Family_JoinedToast"] = "已加入，角色：{0}";
        this["Family_SaveFailed"] = "保存失败";
        this["Family_RoleUpdated"] = "角色已更新为：{0}";

        // ===== DeveloperOptionsView =====
        this["Dev_Title"] = "开发者选项";
        this["Dev_Clear"] = "清空";
        this["Dev_AnimEnabled"] = "启用动画效果";
        this["Dev_AnimHint"] = "关闭可提升性能或满足无障碍需求";
        this["Dev_SearchPlaceholder"] = "搜索 Tag 或内容…";
        this["Dev_FilterAll"] = "全部";
        this["Dev_FilteredCount"] = "筛选后 {0} 条";
        this["Dev_ExportTxt"] = "导出 .txt";
        // DeveloperOptionsViewModel
        this["Dev_SaveFailed"] = "保存设置失败：{0}";
        this["Dev_ExportOk"] = "已导出 {0} 行日志到：{1}";
        this["Dev_ExportFailed"] = "导出失败：{0}";

        // ===== InAppMessageView =====
        this["Msg_Title"] = "应用消息";
        this["Msg_MarkAllRead"] = "全部标记为已读";
        this["Msg_ClearRead"] = "清理已读消息";
        this["Msg_Empty"] = "暂无消息";
        // InAppMessageViewModel
        this["Msg_LoadFailed"] = "加载消息失败";
        this["Msg_AllMarkedRead"] = "已全部标记为已读";
        this["Msg_NoReadToClear"] = "没有已读消息可清理";
        this["Msg_Cleared"] = "已清理 {0} 条已读消息";

        // ===== HelpView =====
        this["Help_Title"] = "使用帮助";
        this["Help_BannerTitle"] = "Ai记";
        this["Help_BannerSubtitle"] = "一句话，记录宝宝的一天";
        this["Help_Section_AiNote"] = "什么是 Ai记";
        this["Help_AiNote_Body1"] = "Ai记 是 Ai记 的核心功能：通过自然语言一句话记录宝宝的日常喂养、睡眠、尿布、体温等信息。";
        this["Help_AiNote_Body2"] = "无需逐个点击表单，只需在首页底部输入框直接说话或打字，AI 会自动解析为结构化记录。";
        this["Help_Section_HowTo"] = "如何使用";
        this["Help_HowTo_Body1"] = "在首页底部输入框输入文字，点右侧发送按钮即可。";
        this["Help_HowTo_Body2"] = "支持一句话记录多条事件，例如：\"11点半睡到12点40，吃了130奶粉，喝10ml水\" 会自动拆分为睡眠、瓶喂、喝水三条记录。";
        this["Help_HowTo_Body3"] = "支持的时间表达：\"8:17\"、\"8点\"、\"8点半\"、\"晚上8点\"、\"下午3点半\"。";
        this["Help_HowTo_Body4"] = "AI 不可用时（未配置或网络异常）会自动降级为本地规则解析，覆盖最常见的喂奶/睡眠/尿布/体温等表述。";
        this["Help_Section_Types"] = "支持的记录类型";
        this["Help_Types_1"] = "· 喂奶：\"喝了120ml奶\"、\"亲喂左10右15分\"、\"吸出80ml\"";
        this["Help_Types_2"] = "· 睡眠：\"睡了30分钟\"、\"11点半睡到12点40\"（自动计算时长）";
        this["Help_Types_3"] = "· 尿布：\"换了尿布便便\"、\"尿布干爽\"、\"又尿又拉\"";
        this["Help_Types_4"] = "· 体温：\"体温38.5度\"、\"发烧37.8℃\"";
        this["Help_Types_5"] = "· 用药/营养：\"吃了半包保泰康颗粒\"、\"吃了1粒维D\"、\"喝了5滴伊可新\"";
        this["Help_Types_6"] = "· 身高体重：\"身高75cm 体重9.5kg\"";
        this["Help_Section_Config"] = "AI 配置";
        this["Help_Config_1"] = "进入「我的」→「AI 分析设置」可配置大模型。";
        this["Help_Config_2"] = "支持 OpenAI 兼容接口（DeepSeek、通义千问、本地 Ollama 等），填入 API 地址、密钥、模型名即可。";
        this["Help_Config_3"] = "可在「AI 分析设置」中点「测试连接」验证配置是否可用。";
        this["Help_Config_4"] = "解析来源可切换：local=本地 LLM 直接解析；server=后端解析接口（需配置同步地址）。";
        this["Help_Section_Notes"] = "注意事项";
        this["Help_Notes_1"] = "AI 解析为辅助功能，关键医疗/用药记录请人工核对后再保存。";
        this["Help_Notes_2"] = "解析结果会保存到本地数据库并自动同步到后端（如已配置同步）。";
        this["Help_Notes_3"] = "若解析失败或结果不符合预期，可手动通过底部 + 按钮选择对应记录类型填写。";
        this["Help_Notes_4"] = "本地规则降级不支持所有复杂表述，建议复杂句子拆分多次输入以提升准确率。";

        // ===== LoginView =====
        this["Login_AppName"] = "成长记录";
        this["Login_AppSlogan"] = "记录宝宝成长的每一天";
        this["Login_Account"] = "账号";
        this["Login_SetAccount"] = "设置账号";
        this["Login_AccountPlaceholder"] = "请输入用户名";
        this["Login_Password"] = "密码";
        this["Login_PasswordPlaceholder"] = "请输入密码";
        this["Login_Nickname"] = "昵称";
        this["Login_NicknamePlaceholder"] = "请输入昵称（选填）";
        this["Login_ServerSettings"] = "服务器设置";
        this["Login_ServerSettingsToggle"] = "服务器设置 ▼";
        this["Login_ServerUrlLabel"] = "服务器地址（留空=纯本地模式）";
        this["Login_ServerUrlHint"] = "注册前先填地址，账号会同步到后端服务器";
        this["Login_LoginBtn"] = "登 录";
        this["Login_RegisterBtn"] = "注 册";
        this["Login_GoRegister"] = "没有账号？去注册";
        this["Login_GoLogin"] = "已有账号？去登录";
        this["Login_LocalMode"] = "本地离线版 · 数据存储于本设备";
        // LoginViewModel
        this["Login_WelcomeTitle"] = "🎉 欢迎注册！已赠送 100 积分";
        this["Login_WelcomeBody"] = "感谢注册 ChildNotes！系统已自动为您赠送 {0} 积分，可用于 AI 喂养分析等高级功能。去「积分任务」签到还能每日领取积分哦。";
        this["Login_OperationFailed"] = "操作失败：{0}";
        this["Login_ErrServerUrl"] = "服务器地址必须以 http:// 或 https:// 开头";

        // ===== LoadingView / MainWindow =====
        this["App_Name"] = "成长记录";
        this["App_Slogan"] = "记录宝宝成长的每一天";

        // ===== PrivacyConsentView =====
        this["Privacy_Title"] = "隐私政策";
        this["Privacy_ViewPolicy"] = "查看《隐私政策》 ›";
        this["Privacy_ViewAgreement"] = "查看《用户协议》 ›";
        this["Privacy_BackSummary"] = "‹ 返回摘要";
        this["Privacy_PolicyTab"] = "隐私政策";
        this["Privacy_AgreementTab"] = "用户协议";
        this["Privacy_Disagree"] = "不同意";
        this["Privacy_AgreeContinue"] = "同意并继续";
        this["Privacy_Close"] = "关闭";

        // ===== QuickInputView =====
        this["QuickInput_Placeholder"] = "用一句话记录，AI 帮你分类";
        this["QuickInput_Send"] = "发送";
        this["QuickInput_MoreTypes"] = "更多记录类型";

        // ===== RecordSheetView =====
        this["RS_FormulaMilk"] = "🍼 配方奶";
        this["RS_Breastfeed"] = "🤱 亲喂";
        this["RS_BottleBreastMilk"] = "🍶 瓶喂母乳";
        this["RS_LeftDuration"] = "← 左侧时长（分钟）";
        this["RS_RightDuration"] = "→ 右侧时长（分钟）";
        this["RS_MilkAmount"] = "奶量（ml）";
        this["RS_MilkAmountPlaceholder"] = "请输入奶量";
        this["RS_RecordDate"] = "记录日期";
        this["RS_RecordTime"] = "记录时间";
        this["RS_NoteOptional"] = "备注（选填）";
        this["RS_NotePlaceholder"] = "如：同时喝水 20ml";
        this["RS_Wet"] = "嘘嘘";
        this["RS_Dirty"] = "便便";
        this["RS_Both"] = "都有";
        this["RS_Dry"] = "干爽";
        this["RS_NoteGenericPlaceholder"] = "补充说明...";
        this["RS_StartDate"] = "开始日期";
        this["RS_StartTime"] = "开始时间";
        this["RS_EndDate"] = "结束日期";
        this["RS_EndTime"] = "结束时间";
        this["RS_Temperature"] = "体温（℃）";
        this["RS_TemperaturePlaceholder"] = "输入体温";
        this["RS_TemperatureFeverWarn"] = "⚠️ 体温 ≥ 37.3℃，保存后将触发发烧追踪";
        this["RS_Height"] = "身高";
        this["RS_Weight"] = "体重";
        this["RS_Supplement"] = "💚 补充剂";
        this["RS_Medicine"] = "💊 用药";
        this["RS_CommonItems"] = "常用内容（右键自定义项可删除）";
        this["RS_CustomAdd"] = "自定义添加";
        this["RS_CustomSupplementPlaceholder"] = "输入其他补充剂/药品...";
        this["RS_AddBtn"] = "+ 添加";
        this["RS_DoseOptional"] = "剂量（选填）";
        this["RS_DosePlaceholder"] = "请输入数量...";
        this["RS_Unit"] = "单位";
        this["RS_UnitPlaceholder"] = "输入单位（如：滴、片、丸）...";
        this["RS_AddCustomBtn"] = "添加";
        this["RS_AddCustom"] = "+ 自定义";
        this["RS_LeftDurationLabel"] = "左侧时长（分钟）";
        this["RS_RightDurationLabel"] = "右侧时长（分钟）";
        this["RS_LeftMilkAmount"] = "左侧奶量（ml）";
        this["RS_RightMilkAmount"] = "右侧奶量（ml）";
        this["RS_TotalMilkAmount"] = "总奶量（ml，选填）";
        this["RS_TotalMilkPlaceholder"] = "不填则自动计算";
        this["RS_CommonFoods"] = "常用辅食（可多选，右键自定义项可删除）";
        this["RS_CustomFoodPlaceholder"] = "输入其他辅食名称...";
        this["RS_Texture"] = "质地";
        this["RS_Texture_Puree"] = "泥糊";
        this["RS_Texture_Minced"] = "碎末";
        this["RS_Texture_Chunk"] = "小块";
        this["RS_AmountOptional"] = "食量（选填）";
        this["RS_Reaction"] = "反应";
        this["RS_Reaction_None"] = "无";
        this["RS_Reaction_Allergy"] = "过敏";
        this["RS_Reaction_Vomit"] = "呕吐";
        this["RS_Reaction_Diarrhea"] = "腹泻";
        this["RS_TemperatureOptional"] = "体温（℃，选填）";
        this["RS_TemperatureFeverHint"] = "如有发热请填写";
        this["RS_TemperatureFeverMonitor"] = "⚠️ 体温 ≥ 37.3℃，建议持续监测并注意补水";
        this["RS_RespiratoryLabel"] = "呼吸道症状（可多选）";
        this["RS_Resp_CoughMild"] = "咳嗽(轻微)";
        this["RS_Resp_CoughSevere"] = "咳嗽(频繁)";
        this["RS_Resp_Sneeze"] = "打喷嚏";
        this["RS_Resp_RunnyNose"] = "流鼻涕";
        this["RS_Resp_StuffyNose"] = "鼻塞";
        this["RS_Resp_RapidBreath"] = "呼吸急促";
        this["RS_DigestiveLabel"] = "消化道异常";
        this["RS_Digestive_SpitMild"] = "溢奶(轻微)";
        this["RS_Digestive_ProjectileVomit"] = "喷射状呕吐(严重)";
        this["RS_OtherAbnormalLabel"] = "其他异常";
        this["RS_OtherAbnormalPlaceholder"] = "如：精神差、哭闹、皮疹、辅食后反应异常等";
        this["RS_MedicationLabel"] = "是否用药";
        this["RS_Medicated"] = "已服用药物";
        this["RS_AddCustomVaccine"] = "添加自定义疫苗";
        this["RS_AddCustomVaccineHint"] = "添加后会进入当前宝宝的疫苗时间线";
        this["RS_VaccineName"] = "疫苗名称";
        this["RS_VaccineNamePlaceholder"] = "如：流感疫苗加强针";
        this["RS_Type"] = "类型";
        this["RS_FreeVaccine"] = "免费疫苗";
        this["RS_PaidVaccine"] = "自费疫苗";
        this["RS_VaccinationTime"] = "接种时间";
        this["RS_VaccinationTimePlaceholder"] = "数值";
        this["RS_PreventDisease"] = "预防疾病";
        this["RS_PreventDiseasePlaceholder"] = "可不填";
        this["RS_AddToTimeline"] = "加入时间线";
        this["RS_Selected"] = "已选择";
        this["RS_TimeAxis"] = "时间";
        this["RS_Done"] = "已打";
        this["RS_Skip"] = "跳过";
        this["RS_ChangeTime"] = "改时间";
        this["RS_CancelSkip"] = "取消跳过";
        this["RS_None"] = "暂无";
        this["RS_ActivityName"] = "活动名称";
        this["RS_ActivityNamePlaceholder"] = "如：翻身练习、户外散步...";
        this["RS_ActivityType"] = "活动类型";
        this["RS_ActivityType_Game"] = "游戏";
        this["RS_ActivityType_Outdoor"] = "户外";
        this["RS_ActivityType_Sport"] = "运动";
        this["RS_EndTimeOptional"] = "结束时间（选填）";
        this["RS_EndTimeHint"] = "留空则用下方时长";
        this["RS_DurationOptional"] = "时长（分钟，选填）";
        this["RS_SaveRecord"] = "保存记录";
        this["RS_ChangeTimeTitle"] = "改时间";
        this["RS_ConfirmCancelTitle"] = "确认取消";
        this["RS_ThinkAgain"] = "再想想";
        this["RS_ConfirmCancel"] = "确认取消";
        this["RS_ConfirmCancelMsg"] = "确定要取消本次操作吗？";
        this["RS_ConfirmCancelHint"] = "取消后该剂次将恢复为待接种状态。";
        this["RS_VaccineDate"] = "接种日期";
        this["RS_VaccineTime"] = "接种时间";
        // RecordSheetView code-behind
        this["RS_DelCustomTitle"] = "删除自定义项";
        this["RS_DelCustomFoodTitle"] = "删除自定义辅食";
        this["RS_DelCustomUnitTitle"] = "删除自定义单位";
        this["RS_DelConfirmMsg"] = "确定删除「{0}」吗？";
        this["RS_DelUnitConfirmMsg"] = "确定删除单位「{0}」吗？";
        // Form validation messages
        this["Form_ErrAbnormalEmpty"] = "请至少填写一项异常症状";
        this["Form_ErrTemperatureRange"] = "请输入有效体温（30-45℃）";
        this["Form_ErrActivityName"] = "请输入活动名称";
        this["Form_ErrBreastDuration"] = "请输入亲喂时长";
        this["Form_ErrMilkAmount"] = "请输入奶量";
        this["Form_ErrFoodName"] = "请输入食物名称";
        this["Form_ErrFoodDuplicate"] = "该食物已存在";
        this["Form_ErrFoodNameOrSelect"] = "请输入或选择食物名称";

        // ===== Complementary default items (kept in Chinese as food names) =====
        // Note: Food names are proper nouns, kept as-is in both languages
    }

    // ===== English =====
    private void FillEn()
    {
        // ===== MainShellView TabBar =====
        this["Tab_Home"] = "Home";
        this["Tab_Feeding"] = "Feeding";
        this["Tab_Growth"] = "Growth";
        this["Tab_Mine"] = "Mine";

        // ===== Common =====
        this["Common_Ok"] = "OK";
        this["Common_Cancel"] = "Cancel";
        this["Common_Back"] = "Back";
        this["Common_Save"] = "Save";
        this["Common_Delete"] = "Delete";
        this["Common_Confirm"] = "Confirm";
        this["Common_Copy"] = "Copy";
        this["Common_Add"] = "Add";
        this["Common_Expand"] = "Expand";
        this["Common_Collapse"] = "Collapse";
        this["Common_Loading"] = "Loading...";
        this["Common_Empty"] = "No data";

        // ===== MineView =====
        this["Mine_Section_Baby"] = "Baby";
        this["Mine_Section_Tools"] = "Tools";
        this["Mine_Section_Settings"] = "Settings";
        this["Mine_BabyManager"] = "Baby Management";
        this["Mine_BabyCount_Suffix"] = " babies";
        this["Mine_Family"] = "Family Management";
        this["Mine_Membership"] = "Membership";
        this["Mine_AiAnalysis"] = "Feeding Analysis";
        this["Mine_AiAnalysis_Sub"] = "Last Week";
        this["Mine_Statistics"] = "Statistics";
        this["Mine_Points"] = "Points & Tasks";
        this["Mine_AiSettings"] = "AI Settings";
        this["Mine_ReminderSettings"] = "Reminders";
        this["Mine_SyncSettings"] = "Data Sync";
        this["Mine_DeveloperOptions"] = "Developer Options";
        this["Mine_PrivacyPolicy"] = "Privacy Policy";
        this["Mine_UserAgreement"] = "User Agreement";
        this["Mine_InAppMessage"] = "Messages";
        this["Mine_Help"] = "Help";
        this["Mine_About"] = "About";
        this["Mine_Logout"] = "Log Out";
        this["Mine_Role_Parent"] = "Parent";
        this["Mine_NotLoggedIn"] = "Not Logged In";
        this["Mine_Membership_Active"] = "Member";
        this["Mine_Membership_Regular"] = "Regular User";

        // ===== LanguageSettingsView =====
        this["Language_Title"] = "Language";
        this["Language_ZhHans"] = "简体中文";
        this["Language_En"] = "English";
        this["Language_Description"] = "Switch the app display language (persists after restart)";

        // ===== HomeView =====
        this["Home_Stats"] = "Stats";
        this["Home_CheckIn"] = "Check-in";
        this["Home_LastFeed"] = "🍼 Last feed";
        this["Home_Diaper"] = "💩 Diaper change";
        this["Home_SleepToday"] = "😴 Slept today";
        this["Home_SleepCount"] = "1 time";
        this["Home_HeightWeight"] = "📏 Height / Weight";
        this["Home_AiChangeHint"] = "—— Baby's changes ~";
        this["Home_VaccineTracking"] = "Vaccine Tracking";
        this["Home_QuickRecord"] = "Log";
        this["Home_AbnormalTracking"] = "Abnormal / Illness";
        this["Home_Record"] = "Record";
        this["Home_ContinueRecord"] = "Continue";
        this["Home_Recovered"] = "Recovered";
        this["Home_DailyTip_Default"] = "Record every day, grow together";
        this["Home_DailyTip_Fever"] = "Baby has a fever, monitor temperature and hydrate";
        this["Home_DailyTip_Diarrhea"] = "Baby has diarrhea, observe and hydrate";
        this["Home_DailyTip_Active"] = "Great recording today, keep it up!";
        this["Home_DailyTip_Empty"] = "No records yet today. Tap a quick action below";
        this["Home_NoBaby"] = "No baby added";
        this["Home_Diaper_Zero"] = "0 times";
        this["Home_DiaperDetail_Zero"] = "Dirty 0 Wet 0";
        this["Home_Sleep_Zero"] = "0h 0min";
        this["Home_Minutes"] = "{0} min";
        this["Home_HoursMinutes"] = "{0}h {1}min";
        this["Home_Days"] = "{0} days";
        this["Home_FeedCount"] = "{0} times {1}ml";
        this["Home_FeedCountNoMl"] = "{0} times";
        this["Home_DiaperCount"] = "{0} times";
        this["Home_DiaperDetail"] = "Dirty {0} Wet {1}";
        this["Home_Ai_GoodTitle"] = "{0} is doing well";
        this["Home_Ai_FeverTitle"] = "{0} has a fever";
        this["Home_Ai_DiarrheaTitle"] = "{0} needs care";
        this["Home_Ai_NoRecordTitle"] = "{0} has no records today";
        this["Home_Ai_NoBabyTitle"] = "No baby added";
        this["Home_Ai_SubtitleGood"] = "Growing happily~";
        this["Home_Ai_SubtitleFever"] = "Current temp {0}℃";
        this["Home_Ai_SubtitleDiarrhea"] = "Diarrhea recorded today";
        this["Home_Ai_SubtitleGreat"] = "Eating well, sleeping well~";
        this["Home_Ai_SubtitleNoRecord"] = "Tap a quick button below to start";
        this["Home_Abnormal_Fever"] = "Fever";
        this["Home_Abnormal_Diarrhea"] = "Diarrhea";
        this["Home_Abnormal_Other"] = "Other abnormal";
        this["Home_Abnormal_Respiratory"] = "Respiratory: {0}";
        this["Home_Abnormal_Vomit"] = "Vomit";
        this["Home_Abnormal_Medicated"] = "Medicated";
        this["Home_Abnormal_Summary"] = "Abnormal record today, please monitor";

        // ===== FeedingView =====
        this["Feeding_GoToday"] = "Today";
        this["Feeding_StatsFeed"] = "Feed {0} · {1}ml";
        this["Feeding_StatsFeedNoMl"] = "Feed {0}";
        this["Feeding_StatsBreast"] = "Breast {0}";
        this["Feeding_StatsDiaper"] = "{0} diaper · Dirty {1} · Wet {2}";
        this["Feeding_StatsWater"] = "Water {0} · {1}ml";
        this["Feeding_StatsSupplement"] = "Supplement {0}";
        this["Feeding_StatsSleep"] = "Sleep {0} min";
        this["Feeding_RecordList"] = "Records";
        this["Feeding_FilterAll"] = "All";
        this["Feeding_FilterFeed"] = "Feed";
        this["Feeding_FilterSleep"] = "Sleep";
        this["Feeding_FilterDiaper"] = "Diaper";
        this["Feeding_FilterActivity"] = "Activity";
        this["Feeding_FilterOther"] = "Other";
        this["Feeding_NoRecords"] = "No records today";
        this["Feeding_DeleteConfirmTitle"] = "Confirm Delete";
        this["Feeding_DeleteConfirmMsg"] = "Delete this record?";
        this["Feeding_Deleted"] = "Record deleted";
        this["Weekday_Mon"] = "Mon";
        this["Weekday_Tue"] = "Tue";
        this["Weekday_Wed"] = "Wed";
        this["Weekday_Thu"] = "Thu";
        this["Weekday_Fri"] = "Fri";
        this["Weekday_Sat"] = "Sat";
        this["Weekday_Sun"] = "Sun";
        this["Rec_Feed_Breast"] = "Breastfeed {0}{1}";
        this["Rec_Feed_BreastLeft"] = "L";
        this["Rec_Feed_BreastRight"] = "R";
        this["Rec_Feed_Bottle"] = "Bottle";
        this["Rec_Feed_BottleExpressed"] = "Bottle (breastmilk)";
        this["Rec_Duration_Min"] = "{0} min";
        this["Rec_Diaper_Wet"] = "Wet";
        this["Rec_Diaper_Dirty"] = "Dirty";
        this["Rec_Diaper_Both"] = "Both";
        this["Rec_Diaper_Default"] = "Diaper change";
        this["Rec_Sleep"] = "Sleep";
        this["Rec_SleepRange"] = "{0} → {1}";
        this["Rec_SleepStart"] = "{0} Start";
        this["Rec_Duration_Long"] = "Total {0}h {1}min";
        this["Rec_Duration_Short"] = "Total {0} min";
        this["Rec_Temperature"] = "Temperature";
        this["Rec_Growth"] = "Growth";
        this["Rec_GrowthHeight"] = "H {0}cm ";
        this["Rec_GrowthWeight"] = "W {0}kg";
        this["Rec_Water"] = "Water";
        this["Rec_WaterExtra"] = "Drinking";
        this["Rec_Pump"] = "Pump";
        this["Rec_Abnormal"] = "Abnormal";
        this["Rec_Activity"] = "Activity";
        this["Rec_Supplement_Medicine"] = "Medicine";
        this["Rec_Supplement_Supplement"] = "Supplement";
        this["Rec_SupplementExtra_Medicine"] = "Medicine";
        this["Rec_SupplementExtra_Supplement"] = "Supplement";
        this["Rec_DoseHalf"] = "Half {0}";
        this["Rec_Complementary"] = "Food";
        this["Rec_Abnormal_Fever"] = "Fever";
        this["Rec_Abnormal_Diarrhea"] = "Diarrhea";
        this["Rec_Abnormal_Vomit"] = "Vomit";
        this["Rec_Abnormal_Medicine"] = "Medicine";
        this["Rec_Abnormal_Respiratory"] = "Respiratory: {0}";
        this["Rec_Abnormal_Medicated"] = "Medicated";
        this["Rec_NotBorn"] = "Not born";
        this["Rec_AgeBornDays"] = "{0} days old";
        this["Rec_AgeMonths"] = "{0}mo {1}d";
        this["Rec_AgeYears"] = "{0}y {1}mo {2}d";

        // ===== GrowthView =====
        this["Growth_Title"] = "Growth";
        this["Growth_Subtitle"] = "Record every precious moment";
        this["Growth_AddMoment"] = "Add moment";
        this["Growth_EmptyTitle"] = "No growth records yet";
        this["Growth_EmptyHint1"] = "Tap \"Add moment\" above";
        this["Growth_EmptyHint2"] = "to add baby's first milestone~";
        this["Growth_AddNow"] = "Add now";
        this["Growth_EditDate"] = "Date";
        this["Growth_EditTitle"] = "Title";
        this["Growth_EditTitlePlaceholder"] = "e.g. First roll, first word...";
        this["Growth_EditContentLabel"] = "Details (optional)";
        this["Growth_EditContentPlaceholder"] = "Record the details of this moment...";
        this["Growth_EditPhotosLabel"] = "Photos (optional, up to 4)";
        this["Growth_EditAddPhotoHint"] = "Tap + to add";
        this["Growth_Uploading"] = "Uploading";
        this["Growth_EditAdd"] = "Edit moment";

        // ===== BabyManagerView =====
        this["BabyMgr_Title"] = "Baby Management";
        this["BabyMgr_EmptyTitle"] = "No baby yet";
        this["BabyMgr_EmptyHint"] = "Add a baby first to view and edit here.";
        this["BabyMgr_Current"] = "Current";
        this["BabyMgr_AddBaby"] = "+ Add baby";
        this["BabyMgr_ChangeAvatar"] = "Tap to change avatar";
        this["BabyMgr_Boy"] = "👦 Boy";
        this["BabyMgr_Girl"] = "👧 Girl";
        this["BabyMgr_Name"] = "Name";
        this["BabyMgr_NamePlaceholder"] = "Enter baby name or nickname";
        this["BabyMgr_Birthday"] = "Birthday";
        this["BabyMgr_BirthdayPlaceholder"] = "Select birth date";
        this["BabyMgr_BabyId"] = "Baby ID";
        this["BabyMgr_DeleteBaby"] = "Delete this baby";
        this["BabyMgr_DeleteTitle"] = "Delete baby";
        this["BabyMgr_DeleteConfirm"] = "Delete \"{0}\"? Records will be kept, but baby info cannot be recovered.";
        this["BabyMgr_AddTitle"] = "Add baby";
        this["BabyMgr_EditTitle"] = "Edit baby";
        this["BabyMgr_ErrName"] = "Please enter baby name";
        this["BabyMgr_ErrBirthday"] = "Please select birth date";
        this["BabyMgr_IdEmpty"] = "Baby ID is empty";
        this["BabyMgr_ClipUnavailable"] = "Clipboard unavailable";
        this["BabyMgr_IdCopied"] = "Baby ID copied";
        this["BabyMgr_PickAvatarTitle"] = "Select avatar";
        this["BabyMgr_PickImageFilter"] = "Image files";

        // ===== BabySetupView =====
        this["BabySetup_Welcome"] = "Welcome to Growth Notes";
        this["BabySetup_Hint"] = "Add baby info to start recording";
        this["BabySetup_Gender"] = "Gender";
        this["BabySetup_Name"] = "Name";
        this["BabySetup_Birthday"] = "Birthday";
        this["BabySetup_Start"] = "Start";
        this["BabySetup_Later"] = "Later";
        this["BabySetup_ErrName"] = "Please enter baby name";
        this["BabySetup_ErrBirthday"] = "Please select birth date";

        // ===== AiAnalysisView =====
        this["AiAnalysis_Title"] = "Feeding Analysis";
        this["AiAnalysis_Subtitle"] = "Baby Feeding Analysis";
        this["AiAnalysis_PointsCost"] = "Points / Cost ";
        this["AiAnalysis_CheckIn"] = "Check-in for points";
        this["AiAnalysis_RangeHint"] = "Select 7 consecutive days for AI analysis";
        this["AiAnalysis_RangeCost"] = "(Costs points each time)";
        this["AiAnalysis_StartPlaceholder"] = "Start";
        this["AiAnalysis_To"] = "to";
        this["AiAnalysis_EndPlaceholder"] = "End";
        this["AiAnalysis_Cancel"] = "Cancel";
        this["AiAnalysis_Records"] = "Analysis records";
        this["AiAnalysis_LoadMore"] = "Load more";
        this["AiAnalysis_AllLoaded"] = "— All loaded —";
        this["AiAnalysis_Empty"] = "No analysis yet";
        this["AiAnalysis_EmptyHint"] = "Select a date range and tap the button above";
        this["AiAnalysis_GeneratedAt"] = "Generated {0}";
        this["AiAnalysis_BackToList"] = "Back to list";
        this["AiAnalysis_RangeTipDefault"] = "Select 7 consecutive days as the analysis range";
        this["AiAnalysis_GenerateNew"] = "Generate new analysis";
        this["AiAnalysis_RangeTooShort"] = "Range must be at least 7 days";
        this["AiAnalysis_RangeTooLong"] = "Range must be at most 7 days";
        this["AiAnalysis_RangeOk"] = "Will analyze records in these 7 consecutive days";
        this["AiAnalysis_AlreadyAnalyzed"] = "Already analyzed";
        this["AiAnalysis_Analyzing"] = "Analyzing...";
        this["AiAnalysis_ErrEnableAi"] = "Please enable AI in settings first";
        this["AiAnalysis_ErrPointsShort"] = "Insufficient points, need {0}, have {1}";
        this["AiAnalysis_ErrPointsShortFull"] = "Insufficient points, need {0}, have {1} (check-in daily for points)";
        this["AiAnalysis_ErrPointsShortFinal"] = "Insufficient points, need {0}, have {1}";
        this["AiAnalysis_Done"] = "Analysis complete";
        this["AiAnalysis_Canceled"] = "Analysis canceled";

        // ===== AiSettingsView =====
        this["AiSettings_Title"] = "AI Settings";
        this["AiSettings_EnableAi"] = "Enable AI analysis";
        this["AiSettings_EnableAiHint"] = "Cannot generate new analysis when off";
        this["AiSettings_LlmConfig"] = "LLM Config";
        this["AiSettings_ApiUrl"] = "API URL";
        this["AiSettings_ModelName"] = "Model name";
        this["AiSettings_ParseService"] = "AI Note Parser";
        this["AiSettings_SourceLabel"] = "Source";
        this["AiSettings_SourceHint"] = "Choose local LLM or backend service for AI Note and feeding analysis";
        this["AiSettings_SourceLocal"] = "Local LLM";
        this["AiSettings_SourceServer"] = "Backend service";
        this["AiSettings_ServerUrl"] = "Backend URL";
        this["AiSettings_Hint"] = "Tip";
        this["AiSettings_HintContent"] = "Backend URL must be configured in Data Sync";
        this["AiSettings_GenParams"] = "Generation params";
        this["AiSettings_Temperature"] = "Temperature";
        this["AiSettings_MaxTokens"] = "Max tokens";
        this["AiSettings_Save"] = "Save";
        this["AiSettings_NotesTitle"] = "Notes";
        this["AiSettings_Note1"] = "• Configure OpenAI-compatible LLM API (OpenAI, DeepSeek, Qwen, Moonshot, etc.)";
        this["AiSettings_Note2"] = "• API URL appends /v1/chat/completions by default; for v2-only models (e.g. Qwen3 on Bailian), fill the full URL, e.g. https://dashscope.aliyuncs.com/api/ais-v2/chat/completions";
        this["AiSettings_Note3"] = "• Local LLMs (Ollama/vLLM/LM Studio) can leave API Key blank";
        this["AiSettings_Note4"] = "• API Key is stored locally only, never uploaded";
        this["AiSettings_Note5"] = "• Higher temperature = more diverse; lower = more deterministic";
        this["AiSettings_Note6"] = "• Re-generate analysis after changing config to take effect";

        // ===== ReminderSettingsView =====
        this["Reminder_Title"] = "Reminders";
        this["Reminder_FeedSection"] = "Feed reminders";
        this["Reminder_FeedEnable"] = "Enable feed reminder";
        this["Reminder_FeedEnableHint"] = "After recording a feed, remind next feed by interval";
        this["Reminder_FeedInterval"] = "Interval";
        this["Reminder_FeedIntervalUnit"] = "{0} hours";
        this["Reminder_FeedIntervalHint"] = "Newborns: 2-3 hours; older babies can extend";
        this["Reminder_SleepSection"] = "Sleep reminders";
        this["Reminder_SleepEnable"] = "Enable sleep reminder";
        this["Reminder_SleepEnableHint"] = "Remind whether to wake baby after threshold";
        this["Reminder_SleepThreshold"] = "Threshold";
        this["Reminder_SleepThresholdHint"] = "Newborns have shorter sleep cycles";
        this["Reminder_Save"] = "Save";
        this["Reminder_NotesTitle"] = "Notes";
        this["Reminder_Note1"] = "• Toggle takes effect immediately; duration changes need Save";
        this["Reminder_Note2"] = "• Applies to new feed/sleep records; existing scheduled reminders not auto-updated";
        this["Reminder_Note3"] = "• Triggers on time even if app is killed (system AlarmManager)";
        this["Reminder_Note4"] = "• Android 13+ requires notification permission";

        // ===== SyncSettingsView =====
        this["Sync_Title"] = "Data Sync";
        this["Sync_SyncLabel"] = "Sync";
        this["Sync_EnableCloud"] = "Enable cloud sync";
        this["Sync_EnableCloudHint"] = "Local data works offline when off";
        this["Sync_Server"] = "Server";
        this["Sync_AddressLabel"] = "URL";
        this["Sync_CurrentEffect"] = "Current";
        this["Sync_NetworkStatus"] = "Network";
        this["Sync_SyncLog"] = "Sync log";
        this["Sync_NoLog"] = "No sync records";
        this["Sync_NotesTitle"] = "Notes";
        this["Sync_Note1"] = "• Local data is primary; works offline";
        this["Sync_Note2"] = "• Server URL and login account are auto-configured";
        this["Sync_Note3"] = "• Auto-syncs on network recovery; new records sync after 5s";
        this["Sync_Note4"] = "• Auto-retry on failure; transient errors need no action";
        this["Sync_Note5"] = "• Conflicts merged by update time (last-write-wins)";
        this["Sync_StatusSuccess"] = "Success";
        this["Sync_StatusFailed"] = "Failed";
        this["Sync_StatusRunning"] = "Running";

        // ===== StatisticsView =====
        this["Stats_Title"] = "Statistics";
        this["Stats_To"] = "to";
        this["Stats_Total"] = "Total";
        this["Stats_Peak"] = "Peak";
        this["Stats_Trend"] = "Trend";
        this["Stats_Detail"] = "Detail";

        // ===== PointsView =====
        this["Points_Title"] = "Points & Tasks";
        this["Points_YourPoints"] = "Your points";
        this["Points_Unit"] = "pts";
        this["Points_Summary"] = "Earned {0} pts · Used {1} pts";
        this["Points_Streak"] = "Daily check-in";
        this["Points_StreakDays"] = "Checked in {0} days in a row";
        this["Points_TasksEarn"] = "Earn points";

        // ===== MembershipView =====
        this["Membership_Title"] = "Membership";
        this["Membership_AiToday"] = "AI Note (today)";
        this["Membership_AiWeek"] = "AI Analysis (this week)";
        this["Membership_ExpireAt"] = "Expires: {0}";
        this["Membership_Benefits"] = "Benefits";
        this["Membership_Benefit1Title"] = "More AI Note quota";
        this["Membership_Benefit1Desc"] = "Regular: 10/day, Member: 100/day";
        this["Membership_Benefit2Title"] = "More AI analysis quota";
        this["Membership_Benefit2Desc"] = "Regular: 1/week, Member: 10/week";
        this["Membership_Benefit3Title"] = "Lottery discount";
        this["Membership_Benefit3Desc"] = "Members get 20% off lottery points cost";
        this["Membership_Benefit4Title"] = "HD photo sync";
        this["Membership_Benefit4Desc"] = "Regular: 1280px/85%, Member: 1920px/92% near-lossless";
        this["Membership_SelectPlan"] = "Select plan";
        this["Membership_Recommended"] = "Recommended";
        this["Membership_Selected"] = "Selected: ";
        this["Membership_Subscribe"] = "Subscribe";
        this["Membership_StatusMember"] = "Member";
        this["Membership_StatusRegular"] = "Regular";
        this["Membership_PlanMonthly"] = "Monthly";
        this["Membership_PlanQuarterly"] = "Quarterly";
        this["Membership_PlanYearly"] = "Yearly";
        this["Membership_PlanNone"] = "None";
        this["Membership_ErrCreateOrder"] = "Failed to create order, check network";
        this["Membership_PaySuccessMock"] = "Payment success (Mock mode)";
        this["Membership_PayFailed"] = "Payment failed: {0}";
        this["Membership_SubscribeOk"] = "Membership activated!";
        this["Membership_OrderClosed"] = "Order closed";
        this["Membership_PayConfirming"] = "Confirming payment, please check status later";

        // ===== FamilyView =====
        this["Family_Title"] = "Family";
        this["Family_Refresh"] = "Refresh";
        this["Family_JoinById"] = "+ Join via Baby ID";
        this["Family_MyRole"] = "My role";
        this["Family_BabyId"] = "Baby ID";
        this["Family_Me"] = "(me)";
        this["Family_Owner"] = "Owner";
        this["Family_JoinFamily"] = "Join family";
        this["Family_JoinPlaceholder"] = "Ask baby owner for ID";
        this["Family_SelectRole"] = "Select role";
        this["Family_JoinNow"] = "Join";
        this["Family_RoleEditorTitle"] = "My role · {0}";
        this["Family_Loading"] = "Loading…";
        this["Family_EmptyNoServer"] = "Cannot connect to server. Configure and enable in Data Sync first";
        this["Family_EmptyNoFamily"] = "No family joined yet";
        this["Family_EmptyError"] = "Load failed: {0}";
        this["Family_EmptyNotLoaded"] = "Not loaded yet";
        this["Family_Refreshed"] = "Refreshed";
        this["Family_ErrIdEmpty"] = "Please enter Baby ID";
        this["Family_ErrJoinFailed"] = "Join failed, check Baby ID or network";
        this["Family_JoinedToast"] = "Joined as: {0}";
        this["Family_SaveFailed"] = "Save failed";
        this["Family_RoleUpdated"] = "Role updated to: {0}";

        // ===== DeveloperOptionsView =====
        this["Dev_Title"] = "Developer Options";
        this["Dev_Clear"] = "Clear";
        this["Dev_AnimEnabled"] = "Enable animations";
        this["Dev_AnimHint"] = "Disable for performance or accessibility";
        this["Dev_SearchPlaceholder"] = "Search tag or content…";
        this["Dev_FilterAll"] = "All";
        this["Dev_FilteredCount"] = "{0} filtered";
        this["Dev_ExportTxt"] = "Export .txt";
        this["Dev_SaveFailed"] = "Save failed: {0}";
        this["Dev_ExportOk"] = "Exported {0} lines to: {1}";
        this["Dev_ExportFailed"] = "Export failed: {0}";

        // ===== InAppMessageView =====
        this["Msg_Title"] = "Messages";
        this["Msg_MarkAllRead"] = "Mark all as read";
        this["Msg_ClearRead"] = "Clear read messages";
        this["Msg_Empty"] = "No messages";
        this["Msg_LoadFailed"] = "Failed to load messages";
        this["Msg_AllMarkedRead"] = "All marked as read";
        this["Msg_NoReadToClear"] = "No read messages to clear";
        this["Msg_Cleared"] = "Cleared {0} read messages";

        // ===== HelpView =====
        this["Help_Title"] = "Help";
        this["Help_BannerTitle"] = "AI Note";
        this["Help_BannerSubtitle"] = "One sentence to record baby's day";
        this["Help_Section_AiNote"] = "What is AI Note";
        this["Help_AiNote_Body1"] = "AI Note is the core feature: record baby's feeding, sleep, diaper, temperature via natural language.";
        this["Help_AiNote_Body2"] = "No need to tap through forms — just type or speak in the input box on Home, AI parses it into structured records.";
        this["Help_Section_HowTo"] = "How to use";
        this["Help_HowTo_Body1"] = "Type in the input box on Home and tap the send button.";
        this["Help_HowTo_Body2"] = "Multiple events in one sentence are supported, e.g. \"slept 11:30-12:40, 130ml formula, 10ml water\" splits into 3 records.";
        this["Help_HowTo_Body3"] = "Time expressions: \"8:17\", \"8 o'clock\", \"8:30\", \"8pm\", \"3:30pm\".";
        this["Help_HowTo_Body4"] = "When AI is unavailable (not configured or network error), falls back to local rule-based parsing for common feed/sleep/diaper/temperature.";
        this["Help_Section_Types"] = "Supported record types";
        this["Help_Types_1"] = "· Feed: \"120ml milk\", \"breast L10 R15 min\", \"pumped 80ml\"";
        this["Help_Types_2"] = "· Sleep: \"slept 30 min\", \"11:30-12:40\" (auto duration)";
        this["Help_Types_3"] = "· Diaper: \"poop diaper\", \"dry diaper\", \"both\"";
        this["Help_Types_4"] = "· Temperature: \"temp 38.5\", \"fever 37.8℃\"";
        this["Help_Types_5"] = "· Medicine/supplement: \"half pack of meds\", \"1 vit D\", \"5 drops\"";
        this["Help_Types_6"] = "· Height/weight: \"height 75cm weight 9.5kg\"";
        this["Help_Section_Config"] = "AI config";
        this["Help_Config_1"] = "Go to Mine → AI Settings to configure LLM.";
        this["Help_Config_2"] = "Supports OpenAI-compatible APIs (DeepSeek, Qwen, local Ollama, etc.). Fill URL, key, model.";
        this["Help_Config_3"] = "Tap \"Test connection\" in AI Settings to verify.";
        this["Help_Config_4"] = "Parser source switchable: local=local LLM; server=backend API (requires sync URL).";
        this["Help_Section_Notes"] = "Notes";
        this["Help_Notes_1"] = "AI parsing is assistive; verify critical medical/medicine records before saving.";
        this["Help_Notes_2"] = "Results save to local DB and auto-sync to backend (if configured).";
        this["Help_Notes_3"] = "If parsing fails, manually tap + to select the record type.";
        this["Help_Notes_4"] = "Local rule fallback doesn't cover all expressions; split complex sentences for accuracy.";

        // ===== LoginView =====
        this["Login_AppName"] = "Growth Notes";
        this["Login_AppSlogan"] = "Record baby's growth every day";
        this["Login_Account"] = "Account";
        this["Login_SetAccount"] = "Set account";
        this["Login_AccountPlaceholder"] = "Enter username";
        this["Login_Password"] = "Password";
        this["Login_PasswordPlaceholder"] = "Enter password";
        this["Login_Nickname"] = "Nickname";
        this["Login_NicknamePlaceholder"] = "Enter nickname (optional)";
        this["Login_ServerSettings"] = "Server settings";
        this["Login_ServerSettingsToggle"] = "Server settings ▼";
        this["Login_ServerUrlLabel"] = "Server URL (empty = local-only)";
        this["Login_ServerUrlHint"] = "Fill URL before register to sync account to server";
        this["Login_LoginBtn"] = "Log In";
        this["Login_RegisterBtn"] = "Register";
        this["Login_GoRegister"] = "No account? Register";
        this["Login_GoLogin"] = "Have an account? Log in";
        this["Login_LocalMode"] = "Local offline · Data stored on device";
        this["Login_WelcomeTitle"] = "🎉 Welcome! 100 points gifted";
        this["Login_WelcomeBody"] = "Thanks for registering ChildNotes! {0} points gifted for AI feeding analysis and more. Check in daily in Points & Tasks for more.";
        this["Login_OperationFailed"] = "Operation failed: {0}";
        this["Login_ErrServerUrl"] = "Server URL must start with http:// or https://";

        // ===== LoadingView / MainWindow =====
        this["App_Name"] = "Growth Notes";
        this["App_Slogan"] = "Record baby's growth every day";

        // ===== PrivacyConsentView =====
        this["Privacy_Title"] = "Privacy Policy";
        this["Privacy_ViewPolicy"] = "View Privacy Policy ›";
        this["Privacy_ViewAgreement"] = "View User Agreement ›";
        this["Privacy_BackSummary"] = "‹ Back to summary";
        this["Privacy_PolicyTab"] = "Privacy Policy";
        this["Privacy_AgreementTab"] = "User Agreement";
        this["Privacy_Disagree"] = "Disagree";
        this["Privacy_AgreeContinue"] = "Agree & Continue";
        this["Privacy_Close"] = "Close";

        // ===== QuickInputView =====
        this["QuickInput_Placeholder"] = "Type a sentence, AI categorizes it";
        this["QuickInput_Send"] = "Send";
        this["QuickInput_MoreTypes"] = "More record types";

        // ===== RecordSheetView =====
        this["RS_FormulaMilk"] = "🍼 Formula";
        this["RS_Breastfeed"] = "🤱 Breastfeed";
        this["RS_BottleBreastMilk"] = "🍶 Bottle breastmilk";
        this["RS_LeftDuration"] = "← Left duration (min)";
        this["RS_RightDuration"] = "→ Right duration (min)";
        this["RS_MilkAmount"] = "Amount (ml)";
        this["RS_MilkAmountPlaceholder"] = "Enter amount";
        this["RS_RecordDate"] = "Date";
        this["RS_RecordTime"] = "Time";
        this["RS_NoteOptional"] = "Note (optional)";
        this["RS_NotePlaceholder"] = "e.g. also 20ml water";
        this["RS_Wet"] = "Wet";
        this["RS_Dirty"] = "Dirty";
        this["RS_Both"] = "Both";
        this["RS_Dry"] = "Dry";
        this["RS_NoteGenericPlaceholder"] = "Additional notes...";
        this["RS_StartDate"] = "Start date";
        this["RS_StartTime"] = "Start time";
        this["RS_EndDate"] = "End date";
        this["RS_EndTime"] = "End time";
        this["RS_Temperature"] = "Temperature (℃)";
        this["RS_TemperaturePlaceholder"] = "Enter temperature";
        this["RS_TemperatureFeverWarn"] = "⚠️ Temp ≥ 37.3℃ will trigger fever tracking";
        this["RS_Height"] = "Height";
        this["RS_Weight"] = "Weight";
        this["RS_Supplement"] = "💚 Supplement";
        this["RS_Medicine"] = "💊 Medicine";
        this["RS_CommonItems"] = "Common items (right-click custom to delete)";
        this["RS_CustomAdd"] = "Custom add";
        this["RS_CustomSupplementPlaceholder"] = "Enter other supplement/medicine...";
        this["RS_AddBtn"] = "+ Add";
        this["RS_DoseOptional"] = "Dose (optional)";
        this["RS_DosePlaceholder"] = "Enter amount...";
        this["RS_Unit"] = "Unit";
        this["RS_UnitPlaceholder"] = "Enter unit (e.g. drops, tablets)...";
        this["RS_AddCustomBtn"] = "Add";
        this["RS_AddCustom"] = "+ Custom";
        this["RS_LeftDurationLabel"] = "Left duration (min)";
        this["RS_RightDurationLabel"] = "Right duration (min)";
        this["RS_LeftMilkAmount"] = "Left amount (ml)";
        this["RS_RightMilkAmount"] = "Right amount (ml)";
        this["RS_TotalMilkAmount"] = "Total amount (ml, optional)";
        this["RS_TotalMilkPlaceholder"] = "Auto-calculated if blank";
        this["RS_CommonFoods"] = "Common foods (multi-select, right-click custom to delete)";
        this["RS_CustomFoodPlaceholder"] = "Enter other food name...";
        this["RS_Texture"] = "Texture";
        this["RS_Texture_Puree"] = "Puree";
        this["RS_Texture_Minced"] = "Minced";
        this["RS_Texture_Chunk"] = "Chunks";
        this["RS_AmountOptional"] = "Amount (optional)";
        this["RS_Reaction"] = "Reaction";
        this["RS_Reaction_None"] = "None";
        this["RS_Reaction_Allergy"] = "Allergy";
        this["RS_Reaction_Vomit"] = "Vomit";
        this["RS_Reaction_Diarrhea"] = "Diarrhea";
        this["RS_TemperatureOptional"] = "Temperature (℃, optional)";
        this["RS_TemperatureFeverHint"] = "Fill if fever";
        this["RS_TemperatureFeverMonitor"] = "⚠️ Temp ≥ 37.3℃, monitor and hydrate";
        this["RS_RespiratoryLabel"] = "Respiratory (multi-select)";
        this["RS_Resp_CoughMild"] = "Cough (mild)";
        this["RS_Resp_CoughSevere"] = "Cough (frequent)";
        this["RS_Resp_Sneeze"] = "Sneeze";
        this["RS_Resp_RunnyNose"] = "Runny nose";
        this["RS_Resp_StuffyNose"] = "Stuffy nose";
        this["RS_Resp_RapidBreath"] = "Rapid breathing";
        this["RS_DigestiveLabel"] = "Digestive";
        this["RS_Digestive_SpitMild"] = "Spit-up (mild)";
        this["RS_Digestive_ProjectileVomit"] = "Projectile vomit (severe)";
        this["RS_OtherAbnormalLabel"] = "Other abnormal";
        this["RS_OtherAbnormalPlaceholder"] = "e.g. lethargy, crying, rash, food reaction";
        this["RS_MedicationLabel"] = "Medicated";
        this["RS_Medicated"] = "Given medicine";
        this["RS_AddCustomVaccine"] = "Add custom vaccine";
        this["RS_AddCustomVaccineHint"] = "Will be added to baby's vaccine timeline";
        this["RS_VaccineName"] = "Vaccine name";
        this["RS_VaccineNamePlaceholder"] = "e.g. flu booster";
        this["RS_Type"] = "Type";
        this["RS_FreeVaccine"] = "Free vaccine";
        this["RS_PaidVaccine"] = "Paid vaccine";
        this["RS_VaccinationTime"] = "Vaccination time";
        this["RS_VaccinationTimePlaceholder"] = "Value";
        this["RS_PreventDisease"] = "Prevents";
        this["RS_PreventDiseasePlaceholder"] = "Optional";
        this["RS_AddToTimeline"] = "Add to timeline";
        this["RS_Selected"] = "Selected";
        this["RS_TimeAxis"] = "Time";
        this["RS_Done"] = "Done";
        this["RS_Skip"] = "Skip";
        this["RS_ChangeTime"] = "Reschedule";
        this["RS_CancelSkip"] = "Unskip";
        this["RS_None"] = "None";
        this["RS_ActivityName"] = "Activity name";
        this["RS_ActivityNamePlaceholder"] = "e.g. tummy time, outdoor walk...";
        this["RS_ActivityType"] = "Activity type";
        this["RS_ActivityType_Game"] = "Play";
        this["RS_ActivityType_Outdoor"] = "Outdoor";
        this["RS_ActivityType_Sport"] = "Sport";
        this["RS_EndTimeOptional"] = "End time (optional)";
        this["RS_EndTimeHint"] = "Blank = use duration below";
        this["RS_DurationOptional"] = "Duration (min, optional)";
        this["RS_SaveRecord"] = "Save";
        this["RS_ChangeTimeTitle"] = "Reschedule";
        this["RS_ConfirmCancelTitle"] = "Confirm cancel";
        this["RS_ThinkAgain"] = "Wait";
        this["RS_ConfirmCancel"] = "Confirm cancel";
        this["RS_ConfirmCancelMsg"] = "Cancel this operation?";
        this["RS_ConfirmCancelHint"] = "After cancel, this dose reverts to pending.";
        this["RS_VaccineDate"] = "Vaccine date";
        this["RS_VaccineTime"] = "Vaccine time";
        this["RS_DelCustomTitle"] = "Delete custom item";
        this["RS_DelCustomFoodTitle"] = "Delete custom food";
        this["RS_DelCustomUnitTitle"] = "Delete custom unit";
        this["RS_DelConfirmMsg"] = "Delete \"{0}\"?";
        this["RS_DelUnitConfirmMsg"] = "Delete unit \"{0}\"?";
        this["Form_ErrAbnormalEmpty"] = "Please fill in at least one symptom";
        this["Form_ErrTemperatureRange"] = "Enter valid temperature (30-45℃)";
        this["Form_ErrActivityName"] = "Please enter activity name";
        this["Form_ErrBreastDuration"] = "Please enter breastfeeding duration";
        this["Form_ErrMilkAmount"] = "Please enter milk amount";
        this["Form_ErrFoodName"] = "Please enter food name";
        this["Form_ErrFoodDuplicate"] = "This food already exists";
        this["Form_ErrFoodNameOrSelect"] = "Please enter or select food name";
    }
}
