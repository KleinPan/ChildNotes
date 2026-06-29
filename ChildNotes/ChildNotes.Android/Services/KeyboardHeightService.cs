using System;
using System.Diagnostics;
using Android.Graphics;
using Android.Views;
using Android.Widget;
// .NET 10 Android 中 Activity 存在歧义（与 System.Diagnostics.Activity 冲突），使用别名
using AndroidActivity = Android.App.Activity;

namespace ChildNotes.Android.Services;

/// <summary>
/// 安卓端软键盘高度监听服务。
/// 通过 ViewTreeObserver.OnGlobalLayoutListener 检测 DecorView 可见区域变化，
/// 计算真实键盘高度（像素），并通过回调通知 Avalonia 层。
///
/// 内置防抖：键盘动画期间 DecorView 高度会快速跳变（如 537→0→537），
/// 忽略短时间内（&lt;200ms）从正值跳回 0 的抖动，避免 Avalonia 层收到错误的"键盘已收起"信号。
/// </summary>
public static class KeyboardHeightService
{
    private static GlobalLayoutListener? _listener;
    private static View? _decorView;

    /// <summary>当前软键盘高度（像素），键盘隐藏时为 0。</summary>
    public static int KeyboardHeightPx { get; private set; }

    /// <summary>键盘高度变化回调（高度像素值）。</summary>
    public static Action<int>? OnKeyboardHeightChanged;

    public static void StartObserving(AndroidActivity activity)
    {
        if (_listener is not null) return;

        _decorView = activity.Window?.DecorView;
        if (_decorView is null) return;

        _listener = new GlobalLayoutListener();
        _decorView.ViewTreeObserver.AddOnGlobalLayoutListener(_listener);
    }

    public static void StopObserving()
    {
        if (_listener is null || _decorView is null) return;

        if (_decorView.Handler != null)
            _decorView.ViewTreeObserver.RemoveOnGlobalLayoutListener(_listener);

        _listener.Dispose();
        _listener = null;
        _decorView = null;
        KeyboardHeightPx = 0;
    }

    private class GlobalLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly Rect _rect = new();
        // 防抖：上次报告正高度的时间戳，用于抑制动画期间的零值抖动
        private static long _lastPositiveTickMs;

        public void OnGlobalLayout()
        {
            if (_decorView is null) return;

            _decorView.GetWindowVisibleDisplayFrame(_rect);
            var screenHeight = _decorView.RootView.Height;
            var rawHeight = screenHeight - _rect.Bottom;

            // 过小值视为无键盘
            var keyboardHeightPx = rawHeight < 100 ? 0 : rawHeight;

            // 防抖：如果当前是 0 但上次报告正值不到 200ms，忽略这次零值
            if (keyboardHeightPx == 0 && KeyboardHeightPx > 0)
            {
                var now = Stopwatch.GetTimestamp();
                var elapsedMs = (now - _lastPositiveTickMs) * 1000 / Stopwatch.Frequency;
                if (elapsedMs < 200) return;
            }

            if (keyboardHeightPx != KeyboardHeightPx)
            {
                KeyboardHeightPx = keyboardHeightPx;
                if (keyboardHeightPx > 0)
                    _lastPositiveTickMs = Stopwatch.GetTimestamp();

                // 用设备真实 DPI 将物理像素转换为 Avalonia 逻辑像素
                var density = _decorView.Resources?.DisplayMetrics?.Density ?? 2.0f;
                var keyboardHeightLp = (int)(keyboardHeightPx / density);

                try { OnKeyboardHeightChanged?.Invoke(keyboardHeightLp); }
                catch { /* 回调异常不影响主流程 */ }
            }
        }
    }
}
