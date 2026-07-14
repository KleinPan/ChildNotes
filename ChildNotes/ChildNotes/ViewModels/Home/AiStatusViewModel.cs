using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;

namespace ChildNotes.ViewModels.Home;

/// <summary>
/// 首页 AI 状态展示 ViewModel：管理状态图标/标题/副标题/轮播提示。
/// 从 HomeViewModel 拆分，职责单一化。
/// </summary>
public partial class AiStatusViewModel : ObservableObject
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    [ObservableProperty] private string _aiStatusIcon = "☀️";
    [ObservableProperty] private string _aiStatusTitle;
    [ObservableProperty] private string _aiStatusSubtitle;
    [ObservableProperty] private string _aiTipText = DailyTipsCatalog.Current.DefaultTip;

    // 轮播提示相关：对齐小程序 good-status 组件 <swiper interval=5000> 行为
    private readonly DispatcherTimer _tipCarouselTimer;
    private IReadOnlyList<string> _currentTipPool = Array.Empty<string>();
    private int _tipCarouselIndex;

    public AiStatusViewModel()
    {
        _tipCarouselTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Normal, OnTipCarouselTick);
        AiStatusTitle = string.Format(_locale.GetString("Home_Ai_GoodTitle", "{0}状态良好"), "小铃铛");
        AiStatusSubtitle = _locale.GetString("Home_Ai_SubtitleGood", "正在快乐成长中~");

        _locale.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(AppLanguage lang)
    {
        // 默认状态时刷新标题/副标题；有宝宝数据时由宿主 RefreshAiStatus 重算
        AiStatusTitle = string.Format(_locale.GetString("Home_Ai_GoodTitle", "{0}状态良好"), "小铃铛");
        AiStatusSubtitle = _locale.GetString("Home_Ai_SubtitleGood", "正在快乐成长中~");
    }

    /// <summary>根据今日统计刷新 AI 状态（从 RefreshAsync 快照调用）。</summary>
    public void RefreshAiStatus(DayStats? stats, string babyName)
    {
        // 配置来源：DailyTipsCatalog.Current（默认值对齐小程序，可由 daily-tips.json 覆盖）
        var cfg = DailyTipsCatalog.Current;
        // 标题拼接对齐小程序 good-status/index.wxml 第 10 行 babyName + '状态良好'
        var name = string.IsNullOrWhiteSpace(babyName) ? cfg.NoBabyTitle : babyName;
        var noBabyTitle = _locale.GetString("Home_Ai_NoBabyTitle", "未添加宝宝");

        if (stats is null)
        {
            AiStatusIcon = "☀️";
            AiStatusTitle = FormatTitle(_locale.GetString("Home_Ai_GoodTitle", "{0}状态良好"), name, noBabyTitle);
            AiStatusSubtitle = _locale.GetString("Home_Ai_SubtitleGood", "正在快乐成长中~");
            SetStaticTip(cfg.DefaultTip);
            return;
        }

        if (stats.HasFever)
        {
            AiStatusIcon = "🌡️";
            AiStatusTitle = FormatTitle(_locale.GetString("Home_Ai_FeverTitle", "{0}体温偏高"), name, noBabyTitle);
            AiStatusSubtitle = string.Format(_locale.GetString("Home_Ai_SubtitleFever", "当前体温{0}℃"), stats.LatestTemperature?.ToString("F1") ?? string.Empty);
            SetStaticTip(cfg.FeverTip);
        }
        else if (stats.HasDiarrhea)
        {
            AiStatusIcon = "⚠️";
            AiStatusTitle = FormatTitle(_locale.GetString("Home_Ai_DiarrheaTitle", "{0}肠胃需呵护"), name, noBabyTitle);
            AiStatusSubtitle = _locale.GetString("Home_Ai_SubtitleDiarrhea", "今日有腹泻记录");
            SetStaticTip(cfg.DiarrheaTip);
        }
        else if (stats.FeedCount >= 6 && stats.SleepTotalMin >= 480)
        {
            AiStatusIcon = "😊";
            AiStatusTitle = FormatTitle(_locale.GetString("Home_Ai_GoodTitle", "{0}状态良好"), name, noBabyTitle);
            AiStatusSubtitle = _locale.GetString("Home_Ai_SubtitleGreat", "吃得好睡得香~");
            StartTipCarousel(cfg.DailyTips);
        }
        else if (stats.FeedCount == 0 && stats.DiaperCount == 0)
        {
            AiStatusIcon = "📝";
            AiStatusTitle = FormatTitle(_locale.GetString("Home_Ai_NoRecordTitle", "{0}今天还没记录"), name, noBabyTitle);
            AiStatusSubtitle = _locale.GetString("Home_Ai_SubtitleNoRecord", "点击下方快捷按钮开始吧");
            SetStaticTip(cfg.DefaultTip);
        }
        else
        {
            AiStatusIcon = "☀️";
            AiStatusTitle = FormatTitle(_locale.GetString("Home_Ai_GoodTitle", "{0}状态良好"), name, noBabyTitle);
            AiStatusSubtitle = _locale.GetString("Home_Ai_SubtitleGood", "正在快乐成长中~");
            StartTipCarousel(cfg.DailyTips);
        }
    }

    /// <summary>重置为默认状态（无宝宝时调用）。</summary>
    public void Reset()
    {
        AiStatusIcon = "☀️";
        AiStatusTitle = string.Format(_locale.GetString("Home_Ai_GoodTitle", "{0}状态良好"), "小铃铛");
        AiStatusSubtitle = _locale.GetString("Home_Ai_SubtitleGood", "正在快乐成长中~");
        SetStaticTip(DailyTipsCatalog.Current.DefaultTip);
    }

    /// <summary>
    /// 用宝宝姓名填充标题模板。
    /// 对齐小程序 babyName + '状态良好' 拼接行为：模板含 {0} 时填充姓名，
    /// 否则原样返回；姓名为空（未添加宝宝）时回退到 NoBabyTitle。
    /// </summary>
    private static string FormatTitle(string template, string babyName, string noBabyTitle)
    {
        if (string.IsNullOrWhiteSpace(babyName)) return noBabyTitle;
        return template.Contains("{0}")
            ? string.Format(template, babyName)
            : template;
    }

    /// <summary>设置单条静态提示并停止轮播（异常/未记录状态）。</summary>
    private void SetStaticTip(string tip)
    {
        _tipCarouselTimer.Stop();
        _currentTipPool = Array.Empty<string>();
        _tipCarouselIndex = 0;
        AiTipText = tip;
    }

    /// <summary>
    /// 启动提示轮播（对齐小程序 good-status 组件 vertical swiper，5 秒间隔、循环播放）。
    /// 每次刷新会重置索引；空池时回退到默认提示。
    /// </summary>
    private void StartTipCarousel(IReadOnlyList<string> tips)
    {
        if (tips.Count == 0)
        {
            SetStaticTip(DailyTipsCatalog.Current.DefaultTip);
            return;
        }

        // 若池内容未变则保持当前索引，避免刷新打断轮播节奏
        if (!ReferenceEquals(_currentTipPool, tips) && !_currentTipPool.SequenceEqual(tips))
        {
            _currentTipPool = tips;
            _tipCarouselIndex = 0;
        }
        else
        {
            _currentTipPool = tips;
        }

        AiTipText = _currentTipPool[_tipCarouselIndex];

        // 构造时已订阅 Tick 回调，这里只需 Stop+Start 重置计时周期
        _tipCarouselTimer.Stop();
        _tipCarouselTimer.Start();
    }

    private void OnTipCarouselTick(object? sender, EventArgs e)
    {
        if (_currentTipPool.Count == 0) return;
        _tipCarouselIndex = (_tipCarouselIndex + 1) % _currentTipPool.Count;
        AiTipText = _currentTipPool[_tipCarouselIndex];
    }
}
