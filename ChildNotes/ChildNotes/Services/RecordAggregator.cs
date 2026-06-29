using ChildNotes.Models;
using ChildNotes.Shared.Constants;

namespace ChildNotes.Services;

/// <summary>
/// 记录聚合原始数据：遍历一组记录后得到的中性统计结果。
/// DayStats（含 bool 标志）与 DayAggregate（仅计数）均可由本结果映射得到，
/// AI 分析报告的统计串也可基于本结果生成，消除三处重复的 switch 逻辑。
/// </summary>
public sealed class RecordAggregate
{
    public int FeedCount { get; set; }
    public int BottleMilkMl { get; set; }
    public int BreastCount { get; set; }
    public int BreastDurationSec { get; set; }
    public int SleepDurationSec { get; set; }
    public int DiaperCount { get; set; }
    public int DirtyDiaperCount { get; set; }
    public int WetDiaperCount { get; set; }
    public int TemperatureCount { get; set; }
    public decimal? LatestTemperature { get; set; }
    public bool HasFever { get; set; }
    public bool HasDiarrhea { get; set; }
    public bool HasOtherAbnormal { get; set; }
    public int SupplementCount { get; set; }
    public int GrowthCount { get; set; }
    public decimal? LatestHeightCm { get; set; }
    public decimal? LatestWeightKg { get; set; }
    public int PumpCount { get; set; }
    public int PumpTotalMl { get; set; }
    public int ComplementaryCount { get; set; }
    public int AbnormalCount { get; set; }
    public int ActivityDurationSec { get; set; }
    public int VaccineCount { get; set; }
}

public static class RecordAggregator
{
    /// <summary>
    /// 遍历一组记录，返回聚合结果。这是唯一的 switch 遍历点。
    /// 关于"最近值"：LatestTemperature / LatestHeightCm / LatestWeightKg 采用"遍历顺序最后一个生效"
    /// （与原 StatisticsService.GetDayStats 行为一致）。
    /// 调用方如需按时间排序后的"最新值"，应在传入前对 records 排序，或自行从原始记录中取最新。
    /// </summary>
    public static RecordAggregate Aggregate(IEnumerable<ChildRecord> records)
    {
        var agg = new RecordAggregate();
        foreach (var r in records)
        {
            switch (r.RecordType)
            {
                case RecordType.Feed:
                    agg.FeedCount++;
                    if (r.RecordSubType == "breast")
                    {
                        agg.BreastCount++;
                        agg.BreastDurationSec += r.DurationSec ?? 0;
                    }
                    else
                    {
                        agg.BottleMilkMl += r.AmountMl ?? 0;
                    }
                    break;
                case RecordType.Diaper:
                    agg.DiaperCount++;
                    if (r.RecordSubType is "dirty" or "both") agg.DirtyDiaperCount++;
                    if (r.RecordSubType is "wet" or "both") agg.WetDiaperCount++;
                    break;
                case RecordType.Sleep:
                    agg.SleepDurationSec += r.DurationSec ?? 0;
                    break;
                case RecordType.Supplement:
                    agg.SupplementCount++;
                    break;
                case RecordType.Pump:
                    agg.PumpCount++;
                    agg.PumpTotalMl += r.AmountMl ?? 0;
                    break;
                case RecordType.Complementary:
                    agg.ComplementaryCount++;
                    break;
                case RecordType.Temperature:
                    agg.TemperatureCount++;
                    agg.LatestTemperature = r.TemperatureValue;
                    if (r.AbnormalFlag == true) agg.HasFever = true;
                    break;
                case RecordType.Growth:
                    agg.GrowthCount++;
                    agg.LatestHeightCm = r.HeightCm;
                    agg.LatestWeightKg = r.WeightKg;
                    break;
                case RecordType.Abnormal:
                    agg.AbnormalCount++;
                    if (r.RecordSubType == "fever") agg.HasFever = true;
                    else if (r.RecordSubType == "diarrhea") agg.HasDiarrhea = true;
                    else agg.HasOtherAbnormal = true;
                    break;
                case RecordType.Activity:
                    agg.ActivityDurationSec += r.DurationSec ?? 0;
                    break;
                case RecordType.Vaccine:
                    agg.VaccineCount++;
                    break;
            }
        }
        return agg;
    }
}
