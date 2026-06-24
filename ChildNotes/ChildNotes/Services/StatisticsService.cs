using ChildNotes.Models;

namespace ChildNotes.Services;

public sealed class DayStats
{
    public int FeedCount { get; set; }
    public int TotalMilk { get; set; }
    public int BreastCount { get; set; }
    public int BreastDurationMin { get; set; }
    public int DiaperCount { get; set; }
    public int DirtyDiaperCount { get; set; }
    public int WetDiaperCount { get; set; }
    public int SupplementCount { get; set; }
    public string SupplementSummary { get; set; } = string.Empty;
    public int SleepTotalMin { get; set; }
    public int PumpCount { get; set; }
    public int PumpTotalMl { get; set; }
    public int ComplementaryCount { get; set; }
    public bool HasFever { get; set; }
    public bool HasDiarrhea { get; set; }
    public bool HasOtherAbnormal { get; set; }
    public decimal? LatestTemperature { get; set; }
}

public sealed class StatisticsService
{
    private readonly RecordService _recordService;

    public StatisticsService(RecordService recordService) => _recordService = recordService;

    public DayStats GetDayStats(DateTime date)
    {
        var records = _recordService.GetByDate(date);
        var stats = new DayStats();

        foreach (var r in records)
        {
            switch (r.RecordType)
            {
                case RecordType.Feed:
                    stats.FeedCount++;
                    if (r.RecordSubType == "breast")
                    {
                        stats.BreastCount++;
                        stats.BreastDurationMin += (r.DurationSec ?? 0) / 60;
                    }
                    else
                    {
                        stats.TotalMilk += r.AmountMl ?? 0;
                    }
                    break;
                case RecordType.Diaper:
                    stats.DiaperCount++;
                    if (r.RecordSubType is "dirty" or "both") stats.DirtyDiaperCount++;
                    if (r.RecordSubType is "wet" or "both") stats.WetDiaperCount++;
                    break;
                case RecordType.Sleep:
                    stats.SleepTotalMin += (r.DurationSec ?? 0) / 60;
                    break;
                case RecordType.Supplement:
                    stats.SupplementCount++;
                    break;
                case RecordType.Pump:
                    stats.PumpCount++;
                    stats.PumpTotalMl += r.AmountMl ?? 0;
                    break;
                case RecordType.Complementary:
                    stats.ComplementaryCount++;
                    break;
                case RecordType.Temperature:
                    stats.LatestTemperature = r.TemperatureValue;
                    if (r.AbnormalFlag == true) stats.HasFever = true;
                    break;
                case RecordType.Abnormal:
                    if (r.RecordSubType == "fever") stats.HasFever = true;
                    else if (r.RecordSubType == "diarrhea") stats.HasDiarrhea = true;
                    else stats.HasOtherAbnormal = true;
                    break;
            }
        }
        return stats;
    }

    public string FormatBreastDuration(int minutes)
    {
        if (minutes <= 0) return "0分钟";
        var h = minutes / 60;
        var m = minutes % 60;
        return h > 0 ? $"{h}小时{m}分钟" : $"{m}分钟";
    }

    public string FormatSleepTotal(int minutes)
    {
        if (minutes <= 0) return "0小时";
        var h = minutes / 60;
        var m = minutes % 60;
        return h > 0 ? $"{h}小时{m}分" : $"{m}分钟";
    }

    public List<DayAggregate> GetDailyAggregates(DateTime start, DateTime end)
    {
        var records = _recordService.GetByDateRange(start, end);
        var byDate = records.GroupBy(r => r.RecordDate.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var list = new List<DayAggregate>();
        for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
        {
            byDate.TryGetValue(d, out var dayRecords);
            list.Add(BuildAggregate(d, dayRecords ?? new()));
        }
        return list;
    }

    private static DayAggregate BuildAggregate(DateTime date, List<ChildRecord> records)
    {
        var agg = new DayAggregate { Date = date };
        foreach (var r in records)
        {
            switch (r.RecordType)
            {
                case RecordType.Feed:
                    agg.FeedCount++;
                    if (r.RecordSubType == "breast") agg.BreastDurationSec += r.DurationSec ?? 0;
                    else agg.TotalMilk += r.AmountMl ?? 0;
                    break;
                case RecordType.Sleep: agg.SleepDurationSec += r.DurationSec ?? 0; break;
                case RecordType.Diaper: agg.DiaperCount++; break;
                case RecordType.Temperature: agg.TemperatureCount++; break;
                case RecordType.Supplement: agg.SupplementCount++; break;
                case RecordType.Growth: agg.GrowthCount++; break;
                case RecordType.Pump: agg.PumpTotalAmount += r.AmountMl ?? 0; break;
                case RecordType.Complementary: agg.ComplementaryCount++; break;
                case RecordType.Abnormal: agg.AbnormalCount++; break;
                case RecordType.Activity: agg.ActivityDurationSec += r.DurationSec ?? 0; break;
                case RecordType.Vaccine: agg.VaccineCount++; break;
            }
        }
        return agg;
    }

    public static double ExtractValue(DayAggregate day, string typeKey) => typeKey switch
    {
        "feed" => day.FeedCount,
        "milk" => day.TotalMilk,
        "breast" => day.BreastDurationSec,
        "sleep" => day.SleepDurationSec,
        "diaper" => day.DiaperCount,
        "temperature" => day.TemperatureCount,
        "supplement" => day.SupplementCount,
        "growth" => day.GrowthCount,
        "pump" => day.PumpTotalAmount,
        "complementary" => day.ComplementaryCount,
        "abnormal" => day.AbnormalCount,
        "activity" => day.ActivityDurationSec,
        "vaccine" => day.VaccineCount,
        _ => 0,
    };

    public static string FormatMetric(double value, string typeKey, string unit)
    {
        if (typeKey is "sleep" or "activity" or "breast")
        {
            var sec = (int)value;
            var h = sec / 3600;
            var m = (sec % 3600) / 60;
            return h > 0 ? $"{h}时{m}分" : $"{m}分";
        }
        if (value >= 1000) return $"{value / 1000:F1}k{unit}";
        return $"{Math.Round(value)}{unit}";
    }
}

public sealed class DayAggregate
{
    public DateTime Date { get; set; }
    public int FeedCount { get; set; }
    public int TotalMilk { get; set; }
    public int BreastDurationSec { get; set; }
    public int SleepDurationSec { get; set; }
    public int DiaperCount { get; set; }
    public int TemperatureCount { get; set; }
    public int SupplementCount { get; set; }
    public int GrowthCount { get; set; }
    public int PumpTotalAmount { get; set; }
    public int ComplementaryCount { get; set; }
    public int AbnormalCount { get; set; }
    public int ActivityDurationSec { get; set; }
    public int VaccineCount { get; set; }
}
