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
/// 采用双轨方案：
/// 1. 主方案：WindowInsetsCompat.Type.ime()（AndroidX Core，Google 官方推荐）
/// 2. 回退方案：ViewTreeObserver.OnGlobalLayoutListener（兼容老设备）
///
/// ★ 重要：adjustResize 模式下，系统会消费 IME insets 来压缩窗口内容，
///   导致部分设备上 imeInsets.Bottom 返回 0。因此主方案失效时自动降级到回退方案。
///   回退方案的关键改进：使用 displayMetrics 而非 RootView.Height 作为屏幕基准，
///   避免状态栏/导航栏导致的偏小计算。
/// </summary>
public static class KeyboardHeightService
{
    private static InsetsListener? _insetsListener;
    private static GlobalLayoutListener? _fallbackListener;
    private static View? _decorView;
    private static AndroidActivity? _activity;

    /// <summary>当前软键盘高度（逻辑像素），键盘隐藏时为 0。</summary>
    public static int KeyboardHeightLp { get; private set; }

    /// <summary>键盘高度变化回调（参数为逻辑像素高度）。</summary>
    public static Action<int>? OnKeyboardHeightChanged;

    /// <summary>上次报告的高度（用于防重复回调）</summary>
    private static int _lastReportedHeightLp;

    public static void StartObserving(AndroidActivity activity)
    {
        if (_insetsListener is not null) return;

        _activity = activity;
        _decorView = activity.Window?.DecorView;
        if (_decorView is null) return;

        // 同时启用两个方案：
        // - WindowInsetsCompat 用于精确获取 IME 高度（Android 21+）
        // - ViewTreeObserver 作为回退（当 adjustResize 消费了 IME insets 时）
        try
        {
            _insetsListener = new InsetsListener();
            ViewCompat.SetOnApplyWindowInsetsListener(_decorView, _insetsListener);
            ViewCompat.RequestApplyInsets(_decorView);
            Debug.WriteLine("[KbSvc] WindowInsetsCompat listener registered");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KbSvc] WindowInsetsCompat failed: {ex.Message}");
        }

        // 始终启用 ViewTreeObserver 作为回退方案
        StartFallbackObserver();
    }

    public static void StopObserving()
    {
        if (_decorView is null) return;

        if (_insetsListener is not null)
        {
            try { ViewCompat.SetOnApplyWindowInsetsListener(_decorView, null); }
            catch { /* ignore */ }
            _insetsListener = null;
        }

        if (_fallbackListener is not null)
        {
            try { _decorView.ViewTreeObserver.RemoveOnGlobalLayoutListener(_fallbackListener); }
            catch { /* ignore */ }
            _fallbackListener?.Dispose();
            _fallbackListener = null;
        }

        _decorView = null;
        _activity = null;
        KeyboardHeightLp = 0;
        _lastReportedHeightLp = 0;
    }

    private static void StartFallbackObserver()
    {
        if (_decorView is null || _fallbackListener is not null) return;
        _fallbackListener = new GlobalLayoutListener();
        _decorView.ViewTreeObserver.AddOnGlobalLayoutListener(_fallbackListener);
        Debug.WriteLine("[KbSvc] ViewTreeObserver fallback listener registered");
    }

    /// <summary>处理高度变化并通知上层（参数为物理像素）</summary>
    private static void ReportHeight(int heightPx, string source)
    {
        // 转换为 Avalonia 逻辑像素
        var density = _decorView?.Resources?.DisplayMetrics?.Density ?? 2.0f;
        var heightLp = (int)(heightPx / density);

        if (heightLp == _lastReportedHeightLp) return;

        _lastReportedHeightLp = heightLp;
        KeyboardHeightLp = heightLp;

        Debug.WriteLine($"[KbSvc] {source} | px={heightPx} lp={heightLp} density={density}");

        try { OnKeyboardHeightChanged?.Invoke(heightLp); }
        catch { /* 回调异常不影响主流程 */ }
    }

    /// <summary>
    /// 主方案：基于 WindowInsetsCompat.Type.ime() 的 IME 高度监听器。
    /// 注意：adjustResize 模式下此回调可能返回 0（系统已消费 insets），
    /// 此时由 ViewTreeObserver 回退方案接管。
    /// </summary>
    private class InsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(View v, WindowInsetsCompat insets)
        {
            var imeInsets = insets.GetInsets(WindowInsetsCompat.Type.Ime());
            var imeHeightPx = imeInsets.Bottom;

            // 仅在确实有值时报告（adjustResize 下可能为 0，交给 fallback 处理）
            if (imeHeightPx >= 50)
            {
                ReportHeight(imeHeightPx, "IME-insets");
            }

            return insets;
        }
    }

    /// <summary>
    /// 回退方案：基于 ViewTreeObserver 的传统高度计算。
    ///
    /// ★ 关键修正（参考 AndroidBug5497Workaround 经典方案）：
    ///   键盘高度 = 屏幕高度 - 可见区域高度(rect.bottom - rect.top)
    ///   而非 键盘高度 = 屏幕高度 - rect.bottom
    ///
    /// 原代码漏减了 rect.top（状态栏高度），导致算出的键盘高度偏大一个状态栏高度，
    /// 弹窗被推得过高，与键盘间出现间隙。
    /// </summary>
    private class GlobalLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly global::Android.Graphics.Rect _rect = new();
        private static long _lastPositiveTickMs;

        public void OnGlobalLayout()
        {
            if (_decorView is null) return;

            _decorView.GetWindowVisibleDisplayFrame(_rect);

            // DecorView 的 RootView 始终是全屏高度（adjustResize 压缩的是子 View）
            var screenHeight = _decorView.RootView.Height;
            // 可见区域高度 = rect.bottom - rect.top（rect.top 通常是状态栏高度）
            var visibleHeight = _rect.Height();
            // 键盘高度 = 屏幕高度 - 可见区域高度
            var rawHeight = screenHeight - visibleHeight;

            // 过小值视为无键盘
            var keyboardHeightPx = rawHeight < 100 ? 0 : rawHeight;

            // 防抖：忽略短时间内从正值跳回 0 的抖动
            if (keyboardHeightPx == 0 && KeyboardHeightLp > 0)
            {
                var now = Stopwatch.GetTimestamp();
                var elapsedMs = (now - _lastPositiveTickMs) * 1000 / Stopwatch.Frequency;
                if (elapsedMs < 200) return;
            }

            if (keyboardHeightPx > 0)
            {
                _lastPositiveTickMs = Stopwatch.GetTimestamp();
                ReportHeight(keyboardHeightPx, "GlobalLayout");
            }
            else if (KeyboardHeightLp > 0)
            {
                ReportHeight(0, "GlobalLayout");
            }
        }
    }
}
