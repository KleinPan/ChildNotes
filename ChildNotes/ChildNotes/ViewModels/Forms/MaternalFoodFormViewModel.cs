using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Models.Dtos;

namespace ChildNotes.ViewModels;

public partial class MaternalFoodFormViewModel : ObservableObject, IRecordFormViewModel
{
    [ObservableProperty] private string _selectedMealType = "breakfast";
    [ObservableProperty] private string _foodsText = string.Empty;
    [ObservableProperty] private string _selectedSuspicionLevel = "none";
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);

    public void SelectMealType(string t) => SelectedMealType = t;
    public void SelectSuspicionLevel(string l) => SelectedSuspicionLevel = l;

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(FoodsText))
        {
            error = "请输入食物";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public MaternalFoodRecordDto BuildDto()
    {
        var foods = FoodsText.Split(new[] { '、', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        return new MaternalFoodRecordDto
        {
            MealType = SelectedMealType,
            Foods = foods,
            SuspicionLevel = SelectedSuspicionLevel,
            Note = Note,
            Time = TimeText,
        };
    }
}
