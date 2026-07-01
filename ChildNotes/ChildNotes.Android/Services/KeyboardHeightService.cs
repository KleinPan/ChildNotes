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
/// 双轨方案（按优先级）：
///   1. ★ 主方案：WindowInsetsCompat.Type.Ime() — Google 官方 API，直接返回 IME 高度
///   2. 回退方案：ViewTreeObserver.OnGlobalLayoutListener — 兼容老设备 / adjustResize 消费了 IME insets 时
///
/// 主方案优势：
///   - 返回的是系统计算的精确 IME 区域高度（不含导航栏、状态栏等）
///   - 不依赖差值计算，无累积误差
///   - Google 官方推荐方式
///
/// 回退方案改进：
///   - 使用 displayMetrics 真实屏幕高度作为基准
///   - 扣除底部系统栏（导航栏）
/// </summary>
public static class KeyboardHeightService
{
    private static InsetsListener? _insetsListener;
    private static GlobalLayoutListener? _fallbackListener;
    private static View? _decorView;

    /// <summary>当前软键盘高度（逻辑像素），键盘隐藏时为 0。</summary>
    public static int KeyboardHeightLp { get; private set; }

    /// <summary>键盘高度变化回调（参数为逻辑像素高度）。</summary>
    public static Action<int>? OnKeyboardHeightChanged;

    /// <summary>上次报告的高度（用于防重复回调）</summary>
    private static int _lastReportedHeightLp;

    /// <summary>上次报告的数据源（用于让主方案优先于回退方案）</summary>
    private static string _lastSource = "";

    public static void StartObserving(AndroidActivity activity)
    {
        if (_insetsListener is not null) return;

        _decorView = activity.Window?.DecorView;
        if (_decorView is null) return;

        // ★ 主方案：WindowInsetsCompat.Type.Ime()
        try
        {
            _insetsListener = new InsetsListener();
            ViewCompat.SetOnApplyWindowInsetsListener(_decorView, _insetsListener);
            ViewCompat.RequestApplyInsets(_decorView);
            Debug.WriteLine("[KbSvc] IME-insets listener registered (primary)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KbSvc] IME-insets failed: {ex.Message}");
        }

        // 回退方案：ViewTreeObserver（仅在主方案失效时生效）
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
        KeyboardHeightLp = 0;
        _lastReportedHeightLp = 0;
        _lastSource = "";
    }

    private static void StartFallbackObserver()
    {
        if (_decorView is null || _fallbackListener is not null) return;
        _fallbackListener = new GlobalLayoutListener();
        _decorView.ViewTreeObserver.AddOnGlobalLayoutListener(_fallbackListener);
        Debug.WriteLine("[KbSvc] GlobalLayout fallback listener registered");
    }

    /// <summary>
    /// 处理高度变化并通知上层。
    /// sourcePriority: "primary" 优先于 "fallback"，防止回退方案覆盖主方案的精确值。
    /// </summary>
    private static void ReportHeight(int heightPx, string source, string sourcePriority)
    {
        var density = _decorView?.Resources?.DisplayMetrics?.Density ?? 2.0f;
        var heightLp = (int)(heightPx / density);

        if (heightLp == _lastReportedHeightLp) return;

        // ★ 优先级控制：如果上次是 primary 来源，fallback 不覆盖相同/相近的值
        if (sourcePriority == "fallback" && _lastSource == "primary" && KeyboardHeightLp > 0)
        {
            // 只有当 fallback 值与当前值差异 > 10lp 时才允许覆盖（防止抖动）
            if (Math.Abs(heightLp - KeyboardHeightLp) <= 10) return;
        }

        _lastReportedHeightLp = heightLp;
        _lastSource = sourcePriority;
        KeyboardHeightLp = heightLp;

        Debug.WriteLine($"[KbSvc] {source} | px={heightPx} lp={heightLp} density={density} priority={sourcePriority}");

        try { OnKeyboardHeightChanged?.Invoke(heightLp); }
        catch { /* ignore */ }
    }

    /// <summary>
    /// ★ 主方案：基于 WindowInsetsCompat.Type.Ime() 的 IME 高度监听器。
    ///
    /// 这是 Google 官方推荐的获取软键盘高度的方式。
    /// 返回值是系统直接计算的 IME inset 高度，精确且不包含导航栏等系统栏。
    ///
    /// 注意：adjustResize 模式下部分设备可能返回 0（系统消费了 insets），
    /// 此时自动降级到 GlobalLayout 回退方案。
    /// </summary>
    private class InsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(View v, WindowInsetsCompat insets)
        {
            var imeInsets = insets.GetInsets(WindowInsetsCompat.Type.Ime());
            var imeHeightPx = imeInsets.Bottom;

            // 阈值降到 10px（约 5lp）：IME insets 通常在键盘弹出时有明确值
            if (imeHeightPx >= 10)
            {
                ReportHeight(imeHeightPx, "IME-insets", "primary");
            }
            else if (imeHeightPx == 0 && KeyboardHeightLp > 0)
            {
                // IME insets 返回 0 表示键盘收回
                ReportHeight(0, "IME-insets", "primary");
            }

            return insets;
        }
    }

    /// <summary>
    /// 回退方案：基于 ViewTreeObserver 的传统高度计算。
    /// 仅在主方案（IME insets）失效时提供数据。
    ///
    /// 改进公式：
    ///   keyboardHeight = displayMetrics.HeightPixels - visibleFrame.Bottom - bottomNavBarInset
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
            var realScreenHeight = metrics.HeightPixels;

            // 扣除底部导航栏
            var windowInsets = ViewCompat.GetRootWindowInsets(_decorView);
            var systemBarInsets = windowInsets?.GetInsets(WindowInsetsCompat.Type.SystemBars());
            var bottomNavBarPx = systemBarInsets?.Bottom ?? 0;

            var rawHeight = realScreenHeight - _rect.Bottom - bottomNavBarPx;
            var keyboardHeightPx = rawHeight < 100 ? 0 : rawHeight;

            // 防抖
            if (keyboardHeightPx == 0 && KeyboardHeightLp > 0)
            {
                var now = Stopwatch.GetTimestamp();
                var elapsedMs = (now - _lastPositiveTickMs) * 1000 / Stopwatch.Frequency;
                if (elapsedMs < 200) return;
            }

            if (keyboardHeightPx > 0)
            {
                _lastPositiveTickMs = Stopwatch.GetTimestamp();
                ReportHeight(keyboardHeightPx, "GlobalLayout", "fallback");
            }
            else if (KeyboardHeightLp > 0 && _lastSource == "fallback")
            {
                // 只有当之前也是 fallback 来源时才由 fallback 报告收回
                // （如果之前是 primary，primary 会自己报告收回）
                ReportHeight(0, "GlobalLayout", "fallback");
            }
        }
    }
}
