using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

/// <summary>
/// 会员中心 ViewModel：展示套餐、会员状态、AI 次数、创建订单并轮询支付结果。
/// 支付流程：
/// 1. 用户选择套餐 → CreateOrder → 获取 orderInfo
/// 2. Android 端调用支付宝 SDK 拉起支付（通过 OpenAlipayApp 事件）
/// 3. 支付完成后轮询订单状态 → 更新会员状态
/// 4. Mock 模式（Windows 调试）：直接模拟支付成功，跳过支付宝 SDK
/// </summary>
public partial class MembershipViewModel : ViewModelBase
{
    private readonly MembershipApiClient _api = ServiceProvider.Instance.MembershipApiClient;
    private readonly LocaleManager _locale = LocaleManager.Instance;

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _expireAtText = string.Empty;
    [ObservableProperty] private int _aiNoteUsedToday;
    [ObservableProperty] private int _aiNoteRemainingToday;
    [ObservableProperty] private int _aiNoteDailyLimit;
    [ObservableProperty] private int _aiAnalysisUsedThisWeek;
    [ObservableProperty] private int _aiAnalysisRemainingThisWeek;
    [ObservableProperty] private int _aiAnalysisWeeklyLimit;
    [ObservableProperty] private decimal _lotteryDiscount = 1m;
    [ObservableProperty] private bool _loading;
    [ObservableProperty] private bool _paying;
    [ObservableProperty] private string? _selectedPlanType;

    public ObservableCollection<MembershipPlanDto> Plans { get; } = new();

    /// <summary>
    /// 请求拉起支付宝 App 支付。参数为 orderInfo 字符串。
    /// 由 MainShellViewModel 订阅，转发到 Android 平台层调支付宝 SDK。
    /// </summary>
    public event Action<string>? OpenAlipayApp;

    /// <summary>
    /// 支付成功后触发，通知 shell 刷新相关页面（如 AI 分析页的次数显示）。
    /// </summary>
    public event Action? PaymentSucceeded;

    /// <summary>加载套餐与会员状态。</summary>
    public async Task LoadAsync()
    {
        Loading = true;
        try
        {
            await Task.WhenAll(LoadPlansAsync(), LoadStatusAsync());
        }
        finally
        {
            Loading = false;
        }
    }

    private async Task LoadPlansAsync()
    {
        var plans = await _api.GetPlansAsync();
        Plans.Clear();
        if (plans is not null)
        {
            foreach (var p in plans) Plans.Add(p);
            // 默认选中推荐套餐，其次选第一个
            SelectedPlanType = plans.FirstOrDefault(p => p.IsRecommended)?.PlanType
                ?? plans.FirstOrDefault()?.PlanType;
        }
    }

    private async Task LoadStatusAsync()
    {
        var status = await _api.GetStatusAsync();
        if (status is null) return;
        ApplyStatus(status);
    }

    private void ApplyStatus(MembershipStatusDto status)
    {
        IsActive = status.IsActive;
        ExpireAtText = string.IsNullOrEmpty(status.ExpireAt)
            ? string.Empty
            : DateTime.Parse(status.ExpireAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        AiNoteUsedToday = status.AiNoteUsedToday;
        AiNoteRemainingToday = status.AiNoteRemainingToday;
        AiNoteDailyLimit = status.AiNoteDailyLimit;
        AiAnalysisUsedThisWeek = status.AiAnalysisUsedThisWeek;
        AiAnalysisRemainingThisWeek = status.AiAnalysisRemainingThisWeek;
        AiAnalysisWeeklyLimit = status.AiAnalysisWeeklyLimit;
        LotteryDiscount = status.LotteryDiscount;
    }

    /// <summary>选择套餐。</summary>
    [RelayCommand]
    private void SelectPlan(string planType)
    {
        SelectedPlanType = planType;
        PayCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 创建订单并发起支付。
    /// Mock 模式（orderInfo 为空）→ 直接轮询订单状态（后端已标记为已支付）。
    /// 正式模式（orderInfo 非空）→ 触发 OpenAlipayApp 事件拉起支付宝 SDK。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPay))]
    private async Task Pay()
    {
        if (string.IsNullOrEmpty(SelectedPlanType) || Paying) return;
        Paying = true;
        try
        {
            var channel = MembershipConstants.ChannelAlipay;
            var resp = await _api.CreateOrderAsync(SelectedPlanType, channel);
            if (resp is null)
            {
                DisplayToast(_locale.GetString("Membership_ErrCreateOrder", "创建订单失败，请检查网络"));
                return;
            }

            if (string.IsNullOrEmpty(resp.PayParams))
            {
                // Mock 模式：后端直接标记为已支付，轮询确认
                DisplayToast(_locale.GetString("Membership_PaySuccessMock", "支付成功（Mock 模式）"));
                await PollOrderStatusAsync(resp.OrderNo);
            }
            else
            {
                // 正式模式：拉起支付宝 SDK
                OpenAlipayApp?.Invoke(resp.PayParams);
                // 轮询订单状态（支付宝 SDK 返回后由前端触发，这里也启动一个延迟轮询兜底）
                _ = PollOrderStatusAsync(resp.OrderNo);
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("Membership", "Pay failed: " + ex);
            DisplayToast(string.Format(_locale.GetString("Membership_PayFailed", "支付失败：{0}"), ex.Message));
        }
        finally
        {
            Paying = false;
        }
    }

    private bool CanPay() => !Paying && !string.IsNullOrEmpty(SelectedPlanType);

    /// <summary>
    /// 轮询订单状态，最多 10 次，每次间隔 2 秒。
    /// 支付成功后刷新会员状态并触发 PaymentSucceeded 事件。
    /// </summary>
    private async Task PollOrderStatusAsync(string orderNo)
    {
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(2000);
            var status = await _api.GetOrderStatusAsync(orderNo);
            if (status is null) continue;

            if (status.Status == MembershipConstants.OrderStatusPaid)
            {
                if (status.Membership is not null)
                    ApplyStatus(status.Membership);
                DisplayToast(_locale.GetString("Membership_SubscribeOk", "会员开通成功！"));
                PaymentSucceeded?.Invoke();
                return;
            }
            if (status.Status == MembershipConstants.OrderStatusClosed)
            {
                DisplayToast(_locale.GetString("Membership_OrderClosed", "订单已关闭"));
                return;
            }
        }
        // 超时未确认，提示用户稍后查看
        DisplayToast(_locale.GetString("Membership_PayConfirming", "支付结果确认中，请稍后查看会员状态"));
    }

    /// <summary>
    /// Android 端支付宝 SDK 回调后调用此方法，手动触发订单状态轮询。
    /// </summary>
    public async Task OnAlipayCallback(string orderNo)
    {
        await PollOrderStatusAsync(orderNo);
    }
}
