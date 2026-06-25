using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ChildNotes.UiDesignCheck.Analysis;
using ChildNotes.ViewModels;
using ChildNotes.Views;

namespace ChildNotes.UiDesignCheck.Capture;

public sealed class CapturedScreen
{
    public string Name { get; set; } = "";
    public Control RootControl { get; set; } = null!;
    public WriteableBitmap? Bitmap { get; set; }
    public ElementInfo VisualTree { get; set; } = null!;
}

public static class ScreenCapturer
{
    public static Control CreateLoginScreen()
    {
        var vm = new LoginViewModel();
        var view = new LoginView();
        view.DataContext = vm;
        return view;
    }

    public static Control CreateHomeScreen()
    {
        var vm = new HomeViewModel();
        vm.Activate();
        return new HomeView { DataContext = vm };
    }

    public static Control CreateFeedingScreen()
    {
        var vm = new FeedingViewModel();
        vm.Activate();
        return new FeedingView { DataContext = vm };
    }

    public static Control CreateGrowthScreen()
    {
        var vm = new GrowthViewModel();
        vm.Activate();
        return new GrowthView { DataContext = vm };
    }

    public static Control CreateMineScreen()
    {
        var vm = new MineViewModel();
        vm.Activate();
        return new MineView { DataContext = vm };
    }

    public static Control CreateStatisticsScreen()
    {
        var vm = new StatisticsViewModel();
        vm.Load();
        return new StatisticsView { DataContext = vm };
    }

    public static Control CreateAiAnalysisScreen()
    {
        var vm = new AiAnalysisViewModel();
        vm.Load();
        return new AiAnalysisView { DataContext = vm };
    }

    public static Control CreateFamilyScreen()
    {
        var vm = new FamilyViewModel();
        vm.Load();
        return new FamilyView { DataContext = vm };
    }

    public static Control CreatePointsScreen()
    {
        try
        {
            var vm = new PointsViewModel();
            vm.Load();
            return new PointsView { DataContext = vm };
        }
        catch (Exception)
        {
            var vm = new PointsViewModel();
            return new PointsView { DataContext = vm };
        }
    }

    public static Control CreateBabySetupScreen()
    {
        var vm = new BabySetupViewModel();
        return new BabySetupView { DataContext = vm };
    }

    public static Control CreateMainShellScreen()
    {
        var vm = new MainShellViewModel();
        vm.ActivateHome();
        return new MainShellView { DataContext = vm };
    }

    public static CapturedScreen Capture(Control content, string name, int width, int height)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content,
            Background = Avalonia.Media.Brushes.White,
            ShowActivated = false,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        var frame = window.CaptureRenderedFrame();
        var tree = VisualTreeExtractor.Extract(content);

        window.Close();
        Dispatcher.UIThread.RunJobs();

        return new CapturedScreen
        {
            Name = name,
            RootControl = content,
            Bitmap = frame,
            VisualTree = tree
        };
    }
}
