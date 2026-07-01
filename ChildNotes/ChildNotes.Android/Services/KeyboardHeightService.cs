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
/// 原理：通过 ViewTreeObserver.OnGlobalLayoutListener 监听布局变化，
/// 用 displayMetrics.HeightPixels - visibleFrame.Bottom 计算键盘高度，
/// 并扣除底部系统栏（导航栏）以获得纯键盘高度。
///
/// View 层（AiNoteView / RecordSheetView）会再扣除 TabBar 高度，
/// 因为测量值是从屏幕底部算起的距离，包含了 TabBar。
/// </summary>
public static class KeyboardHeightService
{
    private static GlobalLayoutListener? _listener;
    private static View? _decorView;

    /// <summary>当前软键盘高度（逻辑像素），键盘隐藏时为 0。</summary>
    public static int KeyboardHeightLp { get; private set; }

    /// <summary>键盘高度变化回调（参数为逻辑像素高度）。</summary>
    public static Action<int>? OnKeyboardHeightChanged;

    /// <summary>上次报告的高度（防重复回调）</summary>
    private static int _lastReportedLp;

    public static void StartObserving(AndroidActivity activity)
    {
        if (_listener is not null) return;

        _decorView = activity.Window?.DecorView;
        if (_decorView is null) return;

        _listener = new GlobalLayoutListener();
        _decorView.ViewTreeObserver.AddOnGlobalLayoutListener(_listener);
        Debug.WriteLine("[KbSvc] GlobalLayout listener registered");
    }

    public static void StopObserving()
    {
        if (_decorView is null || _listener is null) return;

        try { _decorView.ViewTreeObserver.RemoveOnGlobalLayoutListener(_listener); }
        catch { /* ignore */ }
        _listener?.Dispose();
        _listener = null;
        _decorView = null;
        KeyboardHeightLp = 0;
        _lastReportedLp = 0;
    }

    private static void ReportHeight(int heightPx)
    {
        var density = _decorView?.Resources?.DisplayMetrics?.Density ?? 2.0f;
        var heightLp = (int)(heightPx / density);

        if (heightLp == _lastReportedLp) return;
        _lastReportedLp = heightLp;
        KeyboardHeightLp = heightLp;

        Debug.WriteLine($"[KbSvc] GlobalLayout | px={heightPx} lp={heightLp} density={density}");

        try { OnKeyboardHeightChanged?.Invoke(heightLp); }
        catch { /* ignore */ }
    }

    /// <summary>
    /// 基于 ViewTreeObserver 的键盘高度计算。
    ///
    /// 公式：keyboardHeight = displayMetrics.HeightPixels - visibleFrame.Bottom - bottomNavBarInset
    ///
    /// 扣除项说明：
    ///   - bottomNavBarInset: 底部手势导航栏或传统导航键高度
    ///   - TabBar 不在此处扣除（由 View 层动态获取并扣除）
    /// </summary>
    private class GlobalLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly global::Android.Graphics.Rect _rect = new();
        private static long _lastPositiveTickMs;

        public void OnGlobalLayout()
        {
            if (_decorView is null) return;

            _decorView.GetWindowVisibleDisplayFrame(_rect);

            var metrics = _decorView.Resources?.DisplayMetrics;
            if (metrics is null) return;

            // 扣除底部导航栏
            var windowInsets = ViewCompat.GetRootWindowInsets(_decorView);
            var systemBarInsets = windowInsets?.GetInsets(WindowInsetsCompat.Type.SystemBars());
            var bottomNavBarPx = systemBarInsets?.Bottom ?? 0;

            var rawHeight = metrics.HeightPixels - _rect.Bottom - bottomNavBarPx;
            var keyboardHeightPx = rawHeight < 100 ? 0 : rawHeight;

            // 防抖：键盘收回时延迟 200ms 确认
            if (keyboardHeightPx == 0 && KeyboardHeightLp > 0)
            {
                var now = Stopwatch.GetTimestamp();
                var elapsedMs = (now - _lastPositiveTickMs) * 1000 / Stopwatch.Frequency;
                if (elapsedMs < 200) return;
            }

            if (keyboardHeightPx > 0)
                _lastPositiveTickMs = Stopwatch.GetTimestamp();

            ReportHeight(keyboardHeightPx);
        }
    }
}
