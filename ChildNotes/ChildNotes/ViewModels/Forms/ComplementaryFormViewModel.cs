using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Models.Dtos;

namespace ChildNotes.ViewModels;

public partial class ComplementaryFormViewModel : ObservableObject, IRecordFormViewModel
{
    [ObservableProperty] private string _foodName = string.Empty;
    [ObservableProperty] private string _selectedTexture = "puree";
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _selectedReaction = "none";
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);

    public void SelectTexture(string t) => SelectedTexture = t;
    public void SelectReaction(string r) => SelectedReaction = r;

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(FoodName))
        {
            error = "请输入食物名称";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public ComplementaryRecordDto BuildDto() => new()
    {
        FoodName = FoodName,
        Texture = SelectedTexture,
        Amount = AmountText,
        Reaction = SelectedReaction,
        Abnormal = SelectedReaction is "allergy" or "vomit" or "diarrhea",
        Note = Note,
        Time = TimeText,
    };
}
