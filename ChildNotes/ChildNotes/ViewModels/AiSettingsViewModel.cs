using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// AI 分析设置页 ViewModel：管理大模型（LLM）配置。
/// 从 AiAnalysisView 内嵌 Sheet 拆出，作为"我的"模块下的独立设置页。
/// </summary>
public partial class AiSettingsViewModel : ViewModelBase, IActivatable
{
    private readonly AiAnalysisService _aiService = ServiceProvider.Instance.AiAnalysisService;
    private readonly LlmClient _llmClient = ServiceProvider.Instance.LlmClient;
    private readonly SyncConfigRepository _syncRepo = ServiceProvider.Instance.SyncConfigRepository;

    /// <summary>
    /// 后端连通性测试用的共享 HttpClient。
    /// 复用单例避免每次测试都 new HttpClient 导致 socket 耗尽（.NET 推荐做法）。
    /// 超时通过 CancellationTokenSource 控制，不依赖 HttpClient.Timeout（实例共享后不能按请求设置不同超时）。
    /// </summary>
    private static readonly HttpClient SharedHealthHttp = new();

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _configApiBaseUrl = string.Empty;
    [ObservableProperty] private string _configApiKey = string.Empty;
    [ObservableProperty] private string _configModelName = string.Empty;
    [ObservableProperty] private double _configTemperature = 0.7;
    [ObservableProperty] private int _configMaxTokens = 2048;

    /// <summary>AI 服务来源：local=本地 LLM，server=后端解析接口。作用于 Ai记 和 宝宝喂养分析。</summary>
    [ObservableProperty] private string _noteSource = "local";
    /// <summary>UI 双向绑定用：是否使用本地 LLM。setter 同步更新 NoteSource。</summary>
    public bool UseLocalSource
    {
        get => NoteSource == "local";
        set { if (value) NoteSource = "local"; }
    }
    /// <summary>UI 双向绑定用：是否使用后端服务。setter 同步更新 NoteSource。</summary>
    public bool UseServerSource
    {
        get => NoteSource == "server";
        set { if (value) NoteSource = "server"; }
    }
    /// <summary>UI 绑定用：是否使用本地 LLM（只读，控制区块可见性）。</summary>
    public bool IsLocalSource => NoteSource == "local";
    /// <summary>UI 绑定用：是否使用后端服务（只读，控制区块可见性）。</summary>
    public bool IsServerSource => NoteSource == "server";
    /// <summary>UI 绑定用：后端服务器地址（只读展示）。</summary>
    public string ServerUrl => _syncRepo.Get().ServerUrl ?? string.Empty;

    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string _testButtonText = "测试连接";
    [ObservableProperty] private string _testResult = string.Empty;
    [ObservableProperty] private bool _testSuccess;

    public AiSettingsViewModel()
    {
        Title = "AI 分析设置";
    }

    /// <summary>沿用历史 2500ms 显示时长（基类默认 2200ms）。</summary>
    protected override int ToastDurationMs => 2500;

    public void Activate() => Load();

    partial void OnNoteSourceChanged(string value)
    {
        OnPropertyChanged(nameof(IsLocalSource));
        OnPropertyChanged(nameof(IsServerSource));
        OnPropertyChanged(nameof(UseLocalSource));
        OnPropertyChanged(nameof(UseServerSource));
    }

    partial void OnIsTestingChanged(bool value)
    {
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    private void Load()
    {
        var config = _aiService.GetLlmConfig();
        Enabled = config.Enabled;
        ConfigApiBaseUrl = config.ApiBaseUrl;
        ConfigApiKey = config.ApiKey;
        ConfigModelName = config.ModelName;
        ConfigTemperature = config.Temperature;
        ConfigMaxTokens = config.MaxTokens;
        NoteSource = string.IsNullOrEmpty(config.NoteSource) ? "local" : config.NoteSource;
        TestResult = string.Empty;
        TestSuccess = false;
        OnPropertyChanged(nameof(ServerUrl));
    }

    [RelayCommand]
    private void Save()
    {
        var config = new LlmConfig
        {
            ApiBaseUrl = ConfigApiBaseUrl,
            ApiKey = ConfigApiKey,
            ModelName = ConfigModelName,
            Temperature = ConfigTemperature,
            MaxTokens = ConfigMaxTokens,
            Enabled = Enabled,
            NoteSource = NoteSource,
        };
        _aiService.SaveLlmConfig(config);
        DisplayToast("配置已保存");
    }

    /// <summary>
    /// 测试连接：根据当前 NoteSource 选择测试目标。
    /// local=本地 LLM；server=后端服务器连通性。
    /// 测试前会自动保存当前配置，避免用户切换选项后未保存就测试导致状态丢失。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestConnectionAsync()
    {
        if (IsTesting) return;
        IsTesting = true;
        TestButtonText = "测试中...";
        TestResult = string.Empty;
        TestSuccess = false;

        // 测试前先保存当前 UI 配置，确保返回页面后选项不丢失
        Save();

        try
        {
            if (IsServerSource)
            {
                // 后端模式：检查服务器 /health 端点是否可达
                var server = ServerUrl;
                if (string.IsNullOrWhiteSpace(server))
                {
                    TestResult = "未配置同步服务器地址，请先在同步设置中配置";
                    TestSuccess = false;
                    return;
                }
                // 复用共享 HttpClient，超时通过 CTS 控制
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var resp = await SharedHealthHttp.GetAsync(server.TrimEnd('/') + "/health", cts.Token);
                if (resp.IsSuccessStatusCode)
                {
                    TestResult = "后端服务连接正常";
                    TestSuccess = true;
                }
                else
                {
                    TestResult = $"后端响应 HTTP {(int)resp.StatusCode}";
                    TestSuccess = false;
                }
            }
            else
            {
                // 本地模式：测试 LLM 连接
                var config = new LlmConfig
                {
                    ApiBaseUrl = ConfigApiBaseUrl,
                    ApiKey = ConfigApiKey,
                    ModelName = ConfigModelName,
                    Temperature = ConfigTemperature,
                    MaxTokens = ConfigMaxTokens,
                    Enabled = true,
                };
                var msg = await _llmClient.TestConnectionAsync(config);
                TestResult = msg;
                TestSuccess = true;
            }
        }
        catch (Exception ex)
        {
            TestResult = "连接失败：" + ex.Message;
            TestSuccess = false;
        }
        finally
        {
            IsTesting = false;
            TestButtonText = "测试连接";
        }
    }

    private bool CanTest() => !IsTesting;
}
