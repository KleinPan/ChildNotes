using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ChildNotes.Infrastructure;
using ChildNotes.ViewModels;
using ChildNotes.Views;

namespace ChildNotes;

/// <summary>
/// ViewModel → View 映射。完全显式 switch，不使用反射，
/// 兼容 AOT / Trimming（iOS Release、Android Profiled AOT）。
/// 新增 ViewModel 时在此追加一条分支即可。
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
        {
            DevLogger.Log("VL", "Build: param is null");
            return null;
        }
        var typeName = param.GetType().Name;
        DevLogger.Log("VL", $"Build: {typeName}");
        try
        {
            Control view = param switch
            {
                MainShellViewModel => new MainShellView(),
                LoginViewModel => new LoginView(),
                HomeViewModel => new HomeView(),
                FeedingViewModel => new FeedingView(),
                GrowthViewModel => new GrowthView(),
                MineViewModel => new MineView(),
                BabySetupViewModel => new BabySetupView(),
                BabyManagerViewModel => new BabyManagerView(),
                StatisticsViewModel => new StatisticsView(),
                PointsViewModel => new PointsView(),
                AiAnalysisViewModel => new AiAnalysisView(),
                AiSettingsViewModel => new AiSettingsView(),
                AiNoteViewModel => new AiNoteView(),
                SyncSettingsViewModel => new SyncSettingsView(),
                FamilyViewModel => new FamilyView(),
                RecordSheetViewModel => new RecordSheetView(),
                QuickMenuViewModel => new QuickMenuView(),
                DeveloperOptionsViewModel => new DeveloperOptionsView(),
                // MilestoneEditViewModel 是 GrowthView 内嵌表单，不独立导航
                _ => new TextBlock { Text = "View Not Mapped: " + typeName }
            };
            DevLogger.Log("VL", $"Build done: {typeName} -> {view.GetType().Name}");
            return view;
        }
        catch (Exception ex)
        {
            DevLogger.Log("VL", $"Build EXCEPTION for {typeName}");
            DevLogger.Log("VL", ex);
            return new TextBlock { Text = "Build failed: " + ex.Message };
        }
    }

    public bool Match(object? data) => data is ViewModelBase;
}
