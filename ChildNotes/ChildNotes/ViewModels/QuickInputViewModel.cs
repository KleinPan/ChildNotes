using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

/// <summary>
/// 首页底部快捷输入栏 ViewModel。
/// 用户在输入框输入自然语言 → 点发送按钮触发解析保存（Ai 记）；
/// 或点 + 按钮展开下方功能面板选择记录类型。
/// 发送按钮与 + 按钮互斥：有内容时显示发送、隐藏 +；无内容时相反。
/// AI 不可用时（本地 LLM 未启用且服务端地址未配置，两者均未配置）发送按钮置灰并提示。
/// </summary>
public partial class QuickInputViewModel : ViewModelBase
{
    private readonly AiNoteParseService _parseService = new();
    private readonly RecordService _recordService = ServiceProvider.Instance.RecordService;
    private readonly AppState _state = ServiceProvider.Instance.AppState;
    private readonly AiAnalysisService _aiService = ServiceProvider.Instance.AiAnalysisService;

    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private bool _isParsing;

    /// <summary>是否显示"AI 记次数用尽"积分抵扣确认弹窗（三选：积分抵扣继续 / 升级会员 / 取消）。</summary>
    [ObservableProperty] private bool _showOverageConfirm;

    /// <summary>积分抵扣重发用的原文（弹窗确认后用同一文本重发，避免用户已改动输入框内容）。</summary>
    private string? _pendingOverageText;

    /// <summary>解析并保存成功后通知主壳层刷新首页。</summary>
    public event Action? Saved;

    /// <summary>请求跳转到会员中心（AI 记次数用尽时，由 MainShellViewModel 订阅）。</summary>
    public event Action? MembershipRequired;

    /// <summary>请求跳转到积分任务页（积分抵扣积分不足时，由 MainShellViewModel 订阅）。</summary>
    public event Action? PointsRequired;

    /// <summary>请求展开/收起功能面板（由 MainShellViewModel 订阅转发到 QuickMenu）。</summary>
    public event Action? ToggleActionsRequested;

    /// <summary>请求关闭功能面板（由 MainShellViewModel 订阅转发到 QuickMenu）。</summary>
    public event Action? CloseActionsRequested;

    /// <summary>沿用 AiNote 历史时长。</summary>
    protected override int ToastDurationMs => 2500;

    /// <summary>输入框是否有非空内容（用于发送/+ 按钮互斥切换）。</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(InputText);

    /// <summary>
    /// AI 解析是否可用：本地 LLM 已启用 或 服务端 URL 已配置。
    /// 两者都不可用时发送按钮置灰，提示用户先配置 AI。
    /// </summary>
    public bool IsAiAvailable
    {
        get
        {
            var config = _aiService.GetLlmConfig();
            if (config is { Enabled: true }) return true;
            var serverUrl = ServiceProvider.Instance.SyncConfigRepository.Get().ServerUrl;
            return !string.IsNullOrEmpty(serverUrl);
        }
    }

    partial void OnInputTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasContent));
        SendCommand.NotifyCanExecuteChanged();
        // 有内容时强制收起功能面板（避免与发送按钮冲突）
        if (!string.IsNullOrWhiteSpace(value))
            CloseActionsRequested?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private Task Send() => SendCoreAsync((InputText ?? string.Empty).Trim(), usePointsForOverage: false);

    /// <summary>
    /// 解析并保存核心逻辑。
    /// <paramref name="usePointsForOverage"/> 为 true 时表示免费次数用尽后用户选择积分抵扣，
    /// 携带 UsePointsForOverage=true 重新请求同一接口（后端额外扣 5 积分放行本次超限请求）。
    /// 显式传入 text：积分抵扣重发时使用弹窗前保存的原文，避免用户已改动输入框内容。
    /// </summary>
    private async Task SendCoreAsync(string text, bool usePointsForOverage)
    {
        if (IsParsing) return;

        // 未选宝宝时不允许保存
        if (_state.CurrentBaby is null)
        {
            DisplayToast("请先选择宝宝");
            return;
        }

        // AI 不可用时提示配置
        if (!IsAiAvailable)
        {
            DisplayToast("请先在「我的」→「AI 设置」中配置大模型");
            return;
        }

        if (text.Length > 500)
        {
            DisplayToast("记录内容过长（最多 500 字）");
            return;
        }

        // [AI-LOG] 用户输入入口记录：时间戳 + 输入类型 + 具体内容（与 AiNoteParseService 内部日志互补，便于行为追踪）
        DevLogger.Log("QuickInput", $"[AI-LOG] 用户提交 | 时间={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 类型=QuickInput 长度={text.Length} 抵扣={usePointsForOverage} 文本={text}");

        IsParsing = true;
        SendCommand.NotifyCanExecuteChanged();
        try
        {
            // 解析 + 保存全部移到后台线程，避免 HTTP POST / SQLite 写入阻塞 UI 线程。
            // Android 上 HttpClient.SendAsync 的部分操作（DNS/SSL 握手）可能在调用线程
            // 同步执行，即使 await 也无法避免。SaveLocally 的同步 SQLite 写同理。
            var (summary, toastDuration, savedCount) = await Task.Run(async () =>
            {
                var items = await _parseService.ParseAsync(text, null, usePointsForOverage);
                if (items is null || items.Count == 0)
                    return ((string?)null, 0, 0);

                int saved = 0;
                int idx = 0;
                string? lastTime = null;
                foreach (var item in items)
                {
                    idx++;
                    if (string.IsNullOrEmpty(item.RecordType))
                    {
                        DevLogger.Log("QuickInput", $"[AI-LOG] 第{idx}条跳过：RecordType 为空", DevLogger.Level.Warn);
                        continue;
                    }
                    if (string.IsNullOrEmpty(item.Time) && !string.IsNullOrEmpty(lastTime))
                    {
                        item.Time = lastTime;
                        DevLogger.Log("QuickInput", $"[AI-LOG] 第{idx}条继承前一条 time={lastTime}");
                    }
                    else if (!string.IsNullOrEmpty(item.Time))
                    {
                        lastTime = item.Time;
                    }
                    DevLogger.Log("QuickInput", $"[AI-LOG] 第{idx}/{items.Count}条开始写入 type={item.RecordType} sub={item.RecordSubType ?? "-"}");
                    AiNoteParseService.SaveLocally(item, text, _recordService);
                    saved++;
                    DevLogger.Log("QuickInput", $"[AI-LOG] 第{idx}条写入完成 累计 saved={saved}");
                }
                DevLogger.Log("QuickInput", $"[AI-LOG] 全部写入结束 items={items.Count} saved={saved}");
                if (saved == 0)
                    return ((string?)null, 0, 0);

                var source = items[0].Source == ParseSource.Ai ? "AI" : "规则";
                var header = $"[{source}] 已记录 {saved} 条";
                var lines = items
                    .Where(i => !string.IsNullOrEmpty(i.RecordType))
                    .Select(AiNoteParseService.FormatForToast);
                var s = header + "\n" + string.Join("\n", lines);
                var d = Math.Min(6000, 1500 + saved * 700);
                return (s, d, saved);
            });

            if (savedCount == 0)
            {
                DevLogger.Log("QuickInput", "[AI-LOG] saved=0，提示解析失败", DevLogger.Level.Warn);
                DisplayToast("解析失败，请稍后重试");
                return;
            }

            DisplayToast(summary!, toastDuration);
            InputText = string.Empty; // 清空后 HasContent=false 自动恢复 + 按钮
            Saved?.Invoke();
        }
        catch (AiNoteApiException ex) when (ex.IsAiNoteLimitExceeded && !usePointsForOverage)
        {
            // AI 记免费次数用尽：弹积分抵扣三选弹窗（积分抵扣继续 / 升级会员 / 取消），
            // 保存原文供"积分抵扣继续"用同一文本重发
            DevLogger.Log("QuickInput", "[AI-LOG] AI 记次数已用尽，弹积分抵扣确认：" + ex.Message, DevLogger.Level.Warn);
            _pendingOverageText = text;
            ShowOverageConfirm = true;
        }
        catch (AiNoteApiException ex) when (ex.IsInsufficientPoints)
        {
            // 积分抵扣模式下积分不足：提示并引导去积分任务页（签到获取积分）
            DevLogger.Log("QuickInput", "[AI-LOG] 积分抵扣失败，积分不足：" + ex.Message, DevLogger.Level.Warn);
            DisplayToast($"积分不足，需 {MembershipConstants.AiNoteOveragePointsCost} 积分，签到可获取积分");
            PointsRequired?.Invoke();
        }
        catch (AiNoteApiException ex) when (ex.IsAiNoteLimitExceeded)
        {
            // 已带积分抵扣仍返回次数上限（后端不应出现，防御性兜底）
            DevLogger.Log("QuickInput", "[AI-LOG] 抵扣模式下仍返回次数上限：" + ex.Message, DevLogger.Level.Warn);
            DisplayToast("今日 AI 记次数已达上限");
        }
        catch (Exception ex)
        {
            DevLogger.Log("QuickInput", "[AI-LOG] 保存失败：" + ex.GetType().Name + " | " + ex.Message + "\n" + ex.StackTrace, DevLogger.Level.Error);
            DisplayToast("保存失败");
        }
        finally
        {
            IsParsing = false;
            SendCommand.NotifyCanExecuteChanged();
        }
    }

    // ===== AI 记免费次数用尽：积分抵扣三选弹窗（积分抵扣继续 / 升级会员 / 取消） =====

    /// <summary>积分抵扣确认弹窗消息文案（AI 记抵扣单价固定 5 积分/次，无正常积分消耗）。</summary>
    public string OverageMsg => string.Format(
        LocaleManager.Instance.GetString("QuickInput_OverageMsg", "可消耗 {0} 积分抵扣继续"),
        MembershipConstants.AiNoteOveragePointsCost);

    /// <summary>确认积分抵扣：关闭弹窗，用弹窗前保存的原文携带 usePointsForOverage=true 重发。</summary>
    [RelayCommand]
    private async Task ConfirmOverage()
    {
        var text = _pendingOverageText;
        ShowOverageConfirm = false;
        _pendingOverageText = null;
        if (string.IsNullOrWhiteSpace(text)) return;
        await SendCoreAsync(text, usePointsForOverage: true);
    }

    /// <summary>取消积分抵扣弹窗（放弃本次解析保存）。</summary>
    [RelayCommand]
    private void CancelOverage()
    {
        ShowOverageConfirm = false;
        _pendingOverageText = null;
    }

    /// <summary>升级会员：关闭弹窗并跳转会员中心（复用现有 MembershipRequired 事件链路）。</summary>
    [RelayCommand]
    private void UpgradeForOverage()
    {
        ShowOverageConfirm = false;
        _pendingOverageText = null;
        MembershipRequired?.Invoke();
    }

    /// <summary>发送条件：非解析中 + 有内容 + AI 可用</summary>
    public bool CanSend => !IsParsing && !string.IsNullOrWhiteSpace(InputText) && IsAiAvailable;

    /// <summary>切换功能面板展开/收起。</summary>
    [RelayCommand]
    private void ToggleActions() => ToggleActionsRequested?.Invoke();
}
