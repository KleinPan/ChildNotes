using System;
using System.Diagnostics;
using Android.Views;
using AndroidX.Core.View;
// .NET 10 Android 中 Activity 存在歧义（与 System.Diagnostics.Activity 冲突），使用别名
using AndroidActivity = Android.App.Activity;

namespace ChildNotes.Android.Services;

/// <summary>
/// 安卓端软键盘高度监听服务。
///
/// 使用 AndroidX WindowInsetsCompat.Type.ime() 获取精确的 IME 高度（Google 官方推荐方案），
/// 替代旧式 ViewTreeObserver screenHeight-rectBottom 计算。
///
/// 旧方案问题：在部分设备上（尤其是带导航栏/手势导航的机型），
/// screenHeight - rectBottom 的值略小于实际键盘高度，导致 Avalonia 层偏移不足、弹窗与键盘间出现间隙。
///
/// WindowInsets.Type.ime() 直接报告系统级 IME inset，精度更高且不受导航栏干扰。
/// </summary>
public static class KeyboardHeightService
{
    private static InsetsListener? _insetsListener;
    private static GlobalLayoutListener? _fallbackListener;
    private static View? _decorView;
    private static AndroidActivity? _activity;

    /// <summary>当前软键盘高度（像素），键盘隐藏时为 0。</summary>
    public static int KeyboardHeightPx { get; private set; }

    /// <summary>键盘高度变化回调（高度像素值）。</summary>
    public static Action<int>? OnKeyboardHeightChanged;

    /// <summary>上次报告的高度（用于防重复回调）</summary>
    private static int _lastReportedHeight;

    public static void StartObserving(AndroidActivity activity)
    {
        if (_insetsListener is not null) return;

        _activity = activity;
        _decorView = activity.Window?.DecorView;
        if (_decorView is null) return;

        // ★ 主方案：WindowInsetsCompat.Type.ime()（Android 21+ via AndroidX Core）
        // 这是 Google 官方推荐的获取 IME 高度的方式，比 ViewTreeObserver 更精确
        try
        {
            _insetsListener = new InsetsListener();
            ViewCompat.SetOnApplyWindowInsetsListener(_decorView, _insetsListener);
            // 请求重新分发 insets 以立即获取当前状态
            ViewCompat.RequestApplyInsets(_decorView);
            Debug.WriteLine("[KbSvc] Started with WindowInsetsCompat (modern path)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KbSvc] WindowInsetsCompat failed, falling back: {ex.Message}");
            StartFallbackObserver();
        }
    }

    public static void StopObserving()
    {
        if (_decorView is null) return;

        // 清理主监听器
        if (_insetsListener is not null)
        {
            try { ViewCompat.SetOnApplyWindowInsetsListener(_decorView, null); }
            catch { /* ignore */ }
            _insetsListener = null;
        }

        // 清理 fallback 监听器
        if (_fallbackListener is not null && _decorView.Handler != null)
        {
            try { _decorView.ViewTreeObserver.RemoveOnGlobalLayoutListener(_fallbackListener); }
            catch { /* ignore */ }
            _fallbackListener?.Dispose();
            _fallbackListener = null;
        }

        _decorView = null;
        _activity = null;
        KeyboardHeightPx = 0;
        _lastReportedHeight = 0;
    }

    /// <summary>回退方案：使用传统 ViewTreeObserver 方式（当 WindowInsets 不可用时）</summary>
    private static void StartFallbackObserver()
    {
        if (_decorView is null || _fallbackListener is not null) return;
        _fallbackListener = new GlobalLayoutListener();
        _decorView.ViewTreeObserver.AddOnGlobalLayoutListener(_fallbackListener);
        Debug.WriteLine("[KbSvc] Fallback to ViewTreeObserver (legacy path)");
    }

    /// <summary>处理高度变化并通知上层</summary>
    private static void ReportHeight(int heightPx, string source)
    {
        if (heightPx == _lastReportedHeight) return;

        _lastReportedHeight = heightPx;
        KeyboardHeightPx = heightPx;

        if (heightPx > 0)
        {
            // 用设备真实 DPI 将物理像素转换为 Avalonia 逻辑像素
            var density = _decorView?.Resources?.DisplayMetrics?.Density ?? 2.0f;
            var keyboardHeightLp = (int)(heightPx / density);

            Debug.WriteLine(
                $"[KbSvc] {source} | px={heightPx} lp={keyboardHeightLp} density={density}");

            try { OnKeyboardHeightChanged?.Invoke(keyboardHeightLp); }
            catch { /* 回调异常不影响主流程 */ }
        }
        else
        {
            Debug.WriteLine($"[KbSvc] {source} | keyboard hidden");
            try { OnKeyboardHeightChanged?.Invoke(0); }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// 主方案：基于 WindowInsetsCompat.Type.ime() 的 IME 高度监听器。
    /// 这是 Google 官方推荐方式，直接从系统获取 IME inset 值，精度最高。
    /// </summary>
    private class InsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(View v, WindowInsetsCompat insets)
        {
            // 获取 IME 类型的 inset（即软键盘高度）
            var imeInsets = insets.GetInsets(WindowInsetsCompat.Type.Ime());
            var imeHeightPx = imeInsets.Bottom;

            // 过小值视为无键盘（避免误判）
            if (imeHeightPx < 50) imeHeightPx = 0;

            ReportHeight(imeHeightPx, "IME-insets");

            // 不消费 insets，让系统继续分发给子 View
            return insets;
        }
    }

    /// <summary>
    /// 回退方案：基于 ViewTreeObserver 的传统高度计算。
    /// 仅当 WindowInsets API 不可用时使用。
    /// </summary>
    private class GlobalLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly global::Android.Graphics.Rect _rect = new();
        private static long _lastPositiveTickMs;

        public void OnGlobalLayout()
        {
            if (_decorView is null) return;

            _decorView.GetWindowVisibleDisplayFrame(_rect);
            var screenHeight = _decorView.RootView.Height;
            var rawHeight = screenHeight - _rect.Bottom;

            // 过小值视为无键盘
            var keyboardHeightPx = rawHeight < 100 ? 0 : rawHeight;

            // 防抖：忽略短时间内从正值跳回 0 的抖动
            if (keyboardHeightPx == 0 && KeyboardHeightPx > 0)
            {
                var now = Stopwatch.GetTimestamp();
                var elapsedMs = (now - _lastPositiveTickMs) * 1000 / Stopwatch.Frequency;
                if (elapsedMs < 200) return;
            }

            if (keyboardHeightPx != KeyboardHeightPx)
            {
                if (keyboardHeightPx > 0)
                    _lastPositiveTickMs = Stopwatch.GetTimestamp();

                ReportHeight(keyboardHeightPx, "GlobalLayout-fallback");
            }
        }
    }
}
