using System.Text.Json.Serialization;

namespace ChildNotes.Models.Dtos;

public abstract class BaseRecordDto
{
    public long Id { get; set; }
    public string Time { get; set; } = string.Empty;
}

public sealed class FeedRecordDto : BaseRecordDto
{
    public string Type { get; set; } = string.Empty;
    public string? Side { get; set; }
    public int? Duration { get; set; }
    public int? LeftDuration { get; set; }
    public int? RightDuration { get; set; }
    public int? LeftDurationSec { get; set; }
    public int? RightDurationSec { get; set; }
    public string? LeftStartTime { get; set; }
    public string? RightStartTime { get; set; }
    public int? Amount { get; set; }
}

public sealed class DiaperRecordDto : BaseRecordDto
{
    public string Type { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string? UrineColor { get; set; }
    public string? Consistency { get; set; }
    public List<string> Diarrhea { get; set; } = new();
    public bool Abnormal { get; set; }
    public List<string> Photos { get; set; } = new();
}

public sealed class SleepRecordDto : BaseRecordDto
{
    public string StartTime { get; set; } = string.Empty;
    public string? EndTime { get; set; }
    public int? Duration { get; set; }
}

public sealed class TemperatureRecordDto : BaseRecordDto
{
    public decimal Temperature { get; set; }
    public bool IsAbnormal { get; set; }
    public string? Note { get; set; }
}

public sealed class SupplementRecordDto : BaseRecordDto
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Dose { get; set; }
    public string? Note { get; set; }
}

public sealed class GrowthRecordDto : BaseRecordDto
{
    public decimal? Height { get; set; }
    public decimal? Weight { get; set; }
}

public sealed class AbnormalRecordDto : BaseRecordDto
{
    public decimal? Temperature { get; set; }
    public List<string> Respiratory { get; set; } = new();
    public List<string> Diarrhea { get; set; } = new();
    public bool Vomit { get; set; }
    public bool Medicine { get; set; }
    public string? Note { get; set; }
    public List<string> Photos { get; set; } = new();
}

public sealed class PumpRecordDto : BaseRecordDto
{
    public int? LeftDuration { get; set; }
    public int? RightDuration { get; set; }
    public int? LeftAmount { get; set; }
    public int? RightAmount { get; set; }
    public int? TotalAmount { get; set; }
    public string? Note { get; set; }
}

public sealed class ComplementaryRecordDto : BaseRecordDto
{
    public List<string> FoodTypes { get; set; } = new();
    public string? Texture { get; set; }
    public string? FoodName { get; set; }
    public string? Amount { get; set; }
    public string? AmountUnit { get; set; }
    public string? Note { get; set; }
    public List<string> Photos { get; set; } = new();
    public string? Reaction { get; set; }
    public bool Abnormal { get; set; }
}

public sealed class VaccineRecordDto : BaseRecordDto
{
    public string Name { get; set; } = string.Empty;
    public string? NextName { get; set; }
    public string? NextDate { get; set; }
    public string? Note { get; set; }
}

public sealed class ActivityRecordDto : BaseRecordDto
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int? Duration { get; set; }
}

public sealed class MaternalFoodRecordDto : BaseRecordDto
{
    public string? MealType { get; set; }
    public List<string> Foods { get; set; } = new();
    public List<string> CustomFoods { get; set; } = new();
    public string? SuspicionLevel { get; set; }
    public string? Note { get; set; }
    public List<string> Photos { get; set; } = new();
}

public sealed class MilestoneRecordDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string Date { get; set; } = string.Empty;
    public List<string> Photos { get; set; } = new();
}
