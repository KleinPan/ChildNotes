using System;
using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace ChildNotes.Android.Services;

/// <summary>
/// 安卓端软键盘高度监听服务。
/// 通过 ViewTreeObserver.OnGlobalLayoutListener 检测 DecorView 可见区域变化，
/// 计算真实键盘高度（像素），并通过回调通知 Avalonia 层。
///
/// 使用方式：在 MainActivity.OnCreate 中调用 <see cref="StartObserving"/>，
/// 在 OnDestroy 中调用 <see cref="StopObserving"/>。
/// </summary>
public static class KeyboardHeightService
{
    private static GlobalLayoutListener? _listener;
    private static View? _decorView;

    /// <summary>当前软键盘高度（像素），键盘隐藏时为 0。</summary>
    public static int KeyboardHeightPx { get; private set; }

    /// <summary>键盘高度变化回调（高度像素值）。</summary>
    public static Action<int>? OnKeyboardHeightChanged;

    public static void StartObserving(Activity activity)
    {
        if (_listener is not null) return; // 已在监听

        _decorView = activity.Window?.DecorView;
        if (_decorView is null) return;

        _listener = new GlobalLayoutListener(activity);
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
        private readonly Activity _activity;
        private readonly Rect _rect = new();

        public GlobalLayoutListener(Activity activity) => _activity = activity;

        public void OnGlobalLayout()
        {
            if (_decorView is null) return;

            _decorView?.GetWindowVisibleDisplayFrame(_rect);
            // 键盘高度 = 屏幕高度 - 可见区域底部（状态栏 + 内容可见区）
            var screenHeight = _decorView.RootView.Height;
            var keyboardHeight = screenHeight - _rect.Bottom;

            // 考虑到导航栏等：负值或过小值视为键盘未弹出
            if (keyboardHeight < 100)
                keyboardHeight = 0;

            if (keyboardHeight != KeyboardHeightPx)
            {
                KeyboardHeightPx = keyboardHeight;
                try { OnKeyboardHeightChanged?.Invoke(keyboardHeight); }
                catch { /* 回调异常不影响主流程 */ }
            }
        }
    }
}
