using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.ViewModels;

public partial class MainShellViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase _currentTab;
    [ObservableProperty] private bool _isHomeSelected = true;
    [ObservableProperty] private bool _isFeedingSelected;
    [ObservableProperty] private bool _isGrowthSelected;
    [ObservableProperty] private bool _isMineSelected;

    [ObservableProperty] private bool _isRecordSheetOpen;
    [ObservableProperty] private RecordSheetViewModel _recordSheet;
    [ObservableProperty] private bool _isBabySetupOpen;
    [ObservableProperty] private BabySetupViewModel _babySetup;
    [ObservableProperty] private bool _isStatisticsOpen;
    [ObservableProperty] private StatisticsViewModel _statistics;
    [ObservableProperty] private bool _isPointsOpen;
    [ObservableProperty] private PointsViewModel _points;
    [ObservableProperty] private bool _isFamilyOpen;
    [ObservableProperty] private FamilyViewModel _family;
    [ObservableProperty] private bool _isAiAnalysisOpen;
    [ObservableProperty] private AiAnalysisViewModel _aiAnalysis;

    public HomeViewModel Home { get; }
    public FeedingViewModel Feeding { get; }
    public GrowthViewModel Growth { get; }
    public MineViewModel Mine { get; }

    public event Action? LogoutRequested;

    public MainShellViewModel()
    {
        Home = new HomeViewModel();
        Feeding = new FeedingViewModel();
        Growth = new GrowthViewModel();
        Mine = new MineViewModel();
        _currentTab = Home;

        Home.StatisticsRequested += OpenStatistics;
        Home.CheckInRequested += OpenPoints;
        Home.QuickRecordRequested += OpenQuickRecord;
        Mine.LogoutRequested += OnLogout;

        _recordSheet = new RecordSheetViewModel();
        _recordSheet.Saved += OnRecordSaved;

        _babySetup = new BabySetupViewModel();
        _babySetup.Completed += OnBabySetupCompleted;

        _statistics = new StatisticsViewModel();
        _statistics.BackRequested += () => IsStatisticsOpen = false;

        _points = new PointsViewModel();
        _points.BackRequested += () => IsPointsOpen = false;

        _family = new FamilyViewModel();
        _family.BackRequested += () => IsFamilyOpen = false;
        _family.AddBabyRequested += () =>
        {
            IsFamilyOpen = false;
            OpenBabySetup();
        };

        _aiAnalysis = new AiAnalysisViewModel();
        _aiAnalysis.BackRequested += () => IsAiAnalysisOpen = false;
    }

    [RelayCommand]
    private void SwitchTab(string tab)
    {
        IsRecordSheetOpen = false;
        IsBabySetupOpen = false;
        IsStatisticsOpen = false;
        IsPointsOpen = false;
        IsFamilyOpen = false;
        IsAiAnalysisOpen = false;

        IsHomeSelected = tab == "home";
        IsFeedingSelected = tab == "feeding";
        IsGrowthSelected = tab == "growth";
        IsMineSelected = tab == "mine";

        CurrentTab = tab switch
        {
            "home" => Home,
            "feeding" => Feeding,
            "growth" => Growth,
            "mine" => Mine,
            _ => Home,
        };
        if (CurrentTab is IActivatable activatable) activatable.Activate();
    }

    public void ActivateHome()
    {
        CurrentTab = Home;
        Home.Activate();
    }

    public void OpenQuickRecord(string recordType)
    {
        if (ServiceProvider.Instance.AppState.CurrentBaby is null)
        {
            OpenBabySetup();
            return;
        }
        RecordSheet.Open(recordType);
        IsRecordSheetOpen = true;
    }

    public void OpenBabySetup()
    {
        BabySetup.InitForAdd();
        IsBabySetupOpen = true;
    }

    public void OpenBabyEdit(Baby baby)
    {
        BabySetup.InitForEdit(baby);
        IsBabySetupOpen = true;
    }

    public void OpenBabyInfo()
    {
        var currentBaby = ServiceProvider.Instance.AppState.CurrentBaby;
        if (currentBaby is not null)
        {
            OpenBabyEdit(currentBaby);
        }
        else
        {
            OpenBabySetup();
        }
    }

    public void OpenStatistics()
    {
        Statistics.Load();
        IsStatisticsOpen = true;
    }

    public void OpenPoints()
    {
        Points.Load();
        IsPointsOpen = true;
    }

    public void OpenFamily()
    {
        Family.Load();
        IsFamilyOpen = true;
    }

    public void OpenAiAnalysis()
    {
        AiAnalysis.Load();
        IsAiAnalysisOpen = true;
    }

    private void OnRecordSaved()
    {
        IsRecordSheetOpen = false;
        Home.Refresh();
        if (CurrentTab is FeedingViewModel feeding) feeding.Activate();
        // 刷新统计页，避免数据不同步
        Statistics.Load();
    }

    private void OnBabySetupCompleted()
    {
        IsBabySetupOpen = false;
        Home.Refresh();
    }

    private void OnLogout()
    {
        // 关闭所有弹层
        IsRecordSheetOpen = false;
        IsBabySetupOpen = false;
        IsStatisticsOpen = false;
        IsPointsOpen = false;
        IsFamilyOpen = false;
        IsAiAnalysisOpen = false;
        LogoutRequested?.Invoke();
    }

    public void ActivateHomeAfterLogin()
    {
        IsHomeSelected = true;
        IsFeedingSelected = false;
        IsGrowthSelected = false;
        IsMineSelected = false;
        CurrentTab = Home;
        Home.Activate();
    }
}

public interface IActivatable
{
    void Activate();
}
