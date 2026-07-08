namespace ChildNotes.Services;

/// <summary>
/// 应用退出接口：供隐私协议弹窗"不同意"时调用。
///
/// 平台差异：
/// - Android：调 Activity.Finish() 退出（不强制 kill 进程，让系统接管）
/// - iOS：苹果不允许 App 主动退出，实现里可调用 suspend（退到后台）
/// - Desktop：关闭主窗口
///
/// 未注入平台实现时（如设计期）调用为空操作。
/// </summary>
public interface IApplicationExit
{
    /// <summary>请求退出应用。</summary>
    void Exit();
}
