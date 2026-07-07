using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// 首页底部快捷输入栏 ViewModel。
/// 用户在输入框输入自然语言 → 点发送按钮触发解析保存（Ai 记）；
/// 或点 + 按钮展开下方功能面板选择记录类型。
/// 发送按钮与 + 按钮互斥：有内容时显示发送、隐藏 +；无内容时相反。
/// AI 不可用时（未配置本地 LLM 且无服务端地址）发送按钮置灰并提示。
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

        IsParsing = true;
        SendCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await _parseService.ParseAsync(text);
            if (result is null || string.IsNullOrEmpty(result.RecordType))
            {
                DisplayToast("解析失败，请稍后重试");
                return;
            }

            if (!result.Saved)
            {
                // 本地解析路径：直接写本地库
                AiNoteParseService.SaveLocally(result, text, _recordService);
            }
            else
            {
                // 后端解析路径：后端已落库，本地需要主动拉取同步才能看到记录
                // 否则只能等 SyncTrigger 启动同步(8s)或保活同步(15min)才会拉到本地
                _ = ServiceProvider.Instance.SyncTrigger.RunNowAsync();
            }

            var summary = result.Summary ?? "已记录";
            DisplayToast("已记录：" + summary);
            InputText = string.Empty; // 清空后 HasContent=false 自动恢复 + 按钮
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            DevLogger.Log("QuickInput", "保存失败：" + ex.Message);
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
