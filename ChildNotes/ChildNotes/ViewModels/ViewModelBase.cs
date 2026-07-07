using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChildNotes.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    /// <summary>页面标题（供统一顶栏绑定，子类可按需赋值）。</summary>
    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    // ===== 统一的 Toast / 错误提示状态 =====

    /// <summary>Toast 消息文本（供统一 Toast 浮层绑定）。</summary>
    [ObservableProperty] private string _toastMessage = string.Empty;

    /// <summary>是否显示 Toast。</summary>
    [ObservableProperty] private bool _showToast;

    /// <summary>表单错误信息（供表单错误提示区域绑定）。</summary>
    [ObservableProperty] private string _errorMessage = string.Empty;

    /// <summary>Toast 自动隐藏的延迟（毫秒），默认 2200ms。</summary>
    protected virtual int ToastDurationMs => 2200;

    /// <summary>
    /// 显示 Toast，并在 <see cref="ToastDurationMs"/> 后自动隐藏。
    /// 注：方法名与属性 ShowToast 不同（DisplayToast），避免与 [ObservableProperty] 生成的属性冲突。
    /// 使用 CancellationTokenSource + TaskScheduler.FromCurrentSynchronizationContext() 确保 UI 线程更新，
    /// 且快速连续调用时取消上一次的隐藏任务，避免提前关闭。
    /// </summary>
    private CancellationTokenSource? _toastCts;
    protected void DisplayToast(string msg)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new CancellationTokenSource();
        var ct = _toastCts.Token;
        ToastMessage = msg;
        ShowToast = true;
        _ = Task.Delay(ToastDurationMs, ct)
                .ContinueWith(_ => ShowToast = false,
                              CancellationToken.None,
                              TaskContinuationOptions.None,
                              TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ===== 返回事件 =====

    /// <summary>请求关闭本弹层（由 MainShellViewModel 订阅）。</summary>
    public event Action? BackRequested;

    /// <summary>统一的返回命令。子类无需再各自实现 Back 方法。</summary>
    [RelayCommand]
    protected virtual void Back()
    {
        BackRequested?.Invoke();
    }

    /// <summary>供子类触发 BackRequested（保留兼容旧代码中直接 Raise 的场景）。</summary>
    protected void RaiseBackRequested() => BackRequested?.Invoke();
}

/// <summary>
/// ViewModel 集合的扩展方法：统一替换列表内容，消除各处重复的 Clear + Add 循环。
/// </summary>
public static class ViewModelCollectionExtensions
{
    /// <summary>
    /// 用 <paramref name="source"/> 中的元素替换 <paramref name="target"/> 的全部内容。
    /// 相比 Clear + foreach Add，单次方法调用更简洁，且减少多次 PropertyChanged 通知。
    /// </summary>
    public static void ReplaceAll<T>(this ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }
}
