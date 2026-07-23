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

    /// <summary>解析并保存成功后通知主壳层刷新首页。</summary>
    public event Action? Saved;

    /// <summary>请求跳转到会员中心（AI 记次数用尽时，由 MainShellViewModel 订阅）。</summary>
    public event Action? MembershipRequired;

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
    private async Task Send()
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

        var text = (InputText ?? string.Empty).Trim();
        if (text.Length > 500)
        {
            DisplayToast("记录内容过长（最多 500 字）");
            return;
        }

        // [AI-LOG] 用户输入入口记录：时间戳 + 输入类型 + 具体内容（与 AiNoteParseService 内部日志互补，便于行为追踪）
        DevLogger.Log("QuickInput", $"[AI-LOG] 用户提交 | 时间={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 类型=QuickInput 长度={text.Length} 文本={text}");

        IsParsing = true;
        SendCommand.NotifyCanExecuteChanged();
        try
        {
            var items = await _parseService.ParseAsync(text);
            if (items is null || items.Count == 0)
            {
                DisplayToast("解析失败，请稍后重试");
                return;
            }

            // 后端只解析不落库，前端统一写本地库。
            // 本地写入后会触发 SyncTrigger.NotifyWrite → 增量推送到后端
            int saved = 0;
            int idx = 0;
            string? lastTime = null; // 同批次已处理的最后一条非空 time，用于继承给后续无 time 的记录
            foreach (var item in items)
            {
                idx++;
                if (string.IsNullOrEmpty(item.RecordType))
                {
                    DevLogger.Log("QuickInput", $"[AI-LOG] 第{idx}条跳过：RecordType 为空", DevLogger.Level.Warn);
                    continue;
                }
                // 同一批次多条记录时，若当前记录无 time 则继承前一条的 time
                // （server 路径后端已处理；此处覆盖 local LLM / 规则降级路径）
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
            {
                DevLogger.Log("QuickInput", "[AI-LOG] saved=0，提示解析失败", DevLogger.Level.Warn);
                DisplayToast("解析失败，请稍后重试");
                return;
            }

            // 汇总展示：每条记录一行，格式对齐喂养记录卡片信息密度
            // 标注解析来源（AI / 规则降级），时长随条数动态延长
            var source = items[0].Source == ParseSource.Ai ? "AI" : "规则";
            var header = $"[{source}] 已记录 {saved} 条";
            var lines = items
                .Where(i => !string.IsNullOrEmpty(i.RecordType))
                .Select(AiNoteParseService.FormatForToast);
            var summary = header + "\n" + string.Join("\n", lines);
            // 多条记录 Toast 时长：基础 1500ms + 每条 700ms，上限 6000ms
            var duration = Math.Min(6000, 1500 + saved * 700);
            DisplayToast(summary, duration);
            InputText = string.Empty; // 清空后 HasContent=false 自动恢复 + 按钮
            Saved?.Invoke();
        }
        catch (AiNoteApiException ex) when (ex.IsAiNoteLimitExceeded)
        {
            // AI 记次数用尽：提示并跳转会员中心（与 AI 分析次数用尽的处理一致）
            DevLogger.Log("QuickInput", "[AI-LOG] AI 记次数已用尽：" + ex.Message, DevLogger.Level.Warn);
            DisplayToast("今日 AI 记次数已达上限，升级会员可享 100 次/天");
            MembershipRequired?.Invoke();
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

    /// <summary>发送条件：非解析中 + 有内容 + AI 可用</summary>
    public bool CanSend => !IsParsing && !string.IsNullOrWhiteSpace(InputText) && IsAiAvailable;

    /// <summary>切换功能面板展开/收起。</summary>
    [RelayCommand]
    private void ToggleActions() => ToggleActionsRequested?.Invoke();
}
