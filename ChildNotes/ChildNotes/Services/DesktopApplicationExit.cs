using Avalonia.Controls.ApplicationLifetimes;

namespace ChildNotes.Services;

/// <summary>
/// 桌面端（Windows/macOS/Linux）应用退出实现：关闭主窗口。
/// </summary>
public sealed class DesktopApplicationExit : IApplicationExit
{
    public void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
