using System.Text;
using ChildNotes.Core.Common;
using ChildNotes.Core.Config;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Constants;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Data;
using ChildNotes.Infrastructure.External;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

/// <summary>
/// AI 分析服务：固定 7 天区间，按 (user_id, baby_id, range_start_date, range_end_date) 幂等。
/// server 模式下生成分析时先检查每日次数限制，再扣减积分。
/// 幂等命中不消耗次数、不扣积分。
/// </summary>
public class AiAnalysisService : IAiAnalysisService
{
    private const int AnalysisRangeDays = 7;
    private const int MaxSourceTextLength = 60000;

    private readonly ChildNotesDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IBabyAccessService _babyAccess;
    private readonly DeepSeekClient _ai;
    private readonly PointsWalletService _wallet;
    private readonly AiCostOptions _cost;
    private readonly IMembershipService _membership;
    private readonly string _skillPrompt;

    public AiAnalysisService(ChildNotesDbContext db, ICurrentUserService current, IBabyAccessService babyAccess, DeepSeekClient ai, PointsWalletService wallet, AiCostOptions cost, IMembershipService membership)
    {
        _db = db;
        _current = current;
        _babyAccess = babyAccess;
        _ai = ai;
        _wallet = wallet;
        _cost = cost;
        _membership = membership;
        _skillPrompt = LoadSkillPrompt();
    }

    /// <summary>当前 AI 喂养分析消耗的积分数量（由配置动态控制）。</summary>
    public int AnalysisCostPoints => _cost.AnalysisCost;

    public async Task<AiAnalysisRecordDto> GenerateAsync(GenerateAiAnalysisRequest req, string? babyId, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var (start, end) = ResolveAnalysisRange(req);

        var baby = await ResolveBabyAsync(uid, babyId, ct);

        var records = await _db.ChildRecords
            .Where(r => r.BabyId == baby.Id && r.RecordDate >= start && r.RecordDate <= end)
            .OrderBy(r => r.RecordDate).ThenBy(r => r.RecordTime).ThenBy(r => r.Id)
            .ToListAsync(ct);

        var sourceText = BuildSourceText(baby, start, end, records);

        // 幂等命中：同区间 + sourceText 相同 → 直接返回（不扣积分、不消耗次数）
        var existing = await _db.AiAnalysisRecords.FirstOrDefaultAsync(
            a => a.UserId == uid && a.BabyId == baby.Id
                && a.RangeStartDate == start && a.RangeEndDate == end, ct);
        if (existing is not null && existing.SourceText == sourceText)
            return ToDto(existing);

        // 每周次数限制检查（非会员 1 次/周，会员 10 次/周）
        var limit = await _membership.GetAiAnalysisWeeklyLimitAsync(uid, ct);
        var used = await _membership.GetAiAnalysisUsedThisWeekAsync(uid, ct);
        if (used >= limit)
            throw new BusinessException($"本周 AI 分析次数已用完（{used}/{limit}），升级会员可获得更多次数", 400, "AI_LIMIT_EXCEEDED");

        // 调用 AI 前先扣积分（积分不足抛 BusinessException(INSUFFICIENT_POINTS)）
        // ExecuteUpdateAsync 立即落库，无需事务包裹
        await _wallet.ChangeAsync(uid, -_cost.AnalysisCost, ct);

        string analysisText;
        string model;
        try
        {
            // 调用 AI
            var userMessage = "请只基于本次输入 TXT 生成分析，不要引用历史会话中未出现在 TXT 的内容。\n\n"
                + "下面是后端整理的宝宝所选连续7天记录 TXT，请基于这些记录输出分析和建议。\n\n"
                + sourceText;
            (analysisText, model) = await _ai.ChatAsync(_skillPrompt, userMessage, ct);
            if (string.IsNullOrWhiteSpace(analysisText))
                throw new BusinessException("AI 分析响应为空", 502);

            // AI 调用成功后，增加本周使用次数（幂等命中不消耗，此处才消耗）
            try { await _membership.IncrementAiAnalysisUsageAsync(uid, ct); } catch { /* 次数统计失败不阻塞分析 */ }
        }
        catch
        {
            // AI 调用失败：退还已扣积分（best-effort，失败仅记日志不阻塞异常传播）
            try { await _wallet.ChangeAsync(uid, _cost.AnalysisCost, ct); } catch { }
            throw;
        }

        if (existing is not null)
        {
            existing.BabyName = baby.Name;
            existing.SourceText = sourceText;
            existing.SkillPrompt = _skillPrompt;
            existing.AnalysisText = analysisText;
            existing.Model = model;
            await _db.SaveChangesAsync(ct);
            return ToDto(existing);
        }

        var record = new AiAnalysisRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = uid,
            BabyId = baby.Id,
            BabyName = baby.Name,
            RangeStartDate = start,
            RangeEndDate = end,
            SourceText = sourceText,
            SkillPrompt = _skillPrompt,
            AnalysisText = analysisText,
            Model = model,
        };
        _db.AiAnalysisRecords.Add(record);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // 并发竞态：重新查
            existing = await _db.AiAnalysisRecords.FirstOrDefaultAsync(
                a => a.UserId == uid && a.BabyId == baby.Id
                    && a.RangeStartDate == start && a.RangeEndDate == end, ct);
            if (existing is not null)
            {
                existing.SourceText = sourceText;
                existing.AnalysisText = analysisText;
                existing.Model = model;
                await _db.SaveChangesAsync(ct);
                return ToDto(existing);
            }
            throw;
        }
        return ToDto(record);
    }

    public async Task<List<AiAnalysisRecordDto>> ListAsync(string? babyId, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var targetBabyId = await ResolveBabyIdForQueryAsync(uid, babyId, ct);
        var list = await _db.AiAnalysisRecords
            .Where(a => a.UserId == uid && (targetBabyId == null || a.BabyId == targetBabyId))
            .OrderByDescending(a => a.RangeStartDate)
            .ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<AiAnalysisRecordDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var rec = await _db.AiAnalysisRecords.FirstOrDefaultAsync(a => a.Id == id && a.UserId == uid, ct);
        return rec is null ? null : ToDto(rec);
    }

    private (DateTime start, DateTime end) ResolveAnalysisRange(GenerateAiAnalysisRequest req)
    {
        // 用 UTC 日期避免 Npgsql 写 timestamptz 时报
        // "Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'"
        // 与 RecordService/SyncService 中 SpecifyKind(UTC) 保持一致。
        var today = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Utc);
        if (string.IsNullOrEmpty(req.StartDate) && string.IsNullOrEmpty(req.EndDate))
        {
            var end = today;
            var start = today.AddDays(-(AnalysisRangeDays - 1));
            return (start, end);
        }
        if (string.IsNullOrEmpty(req.StartDate) || string.IsNullOrEmpty(req.EndDate))
            throw new BusinessException("请选择完整的分析时间范围");

        if (!DateTime.TryParse(req.StartDate, out var s) || !DateTime.TryParse(req.EndDate, out var e))
            throw new BusinessException("日期格式不正确，请使用 yyyy-MM-dd");

        if (e > today) throw new BusinessException("结束日期不能晚于今天");
        var days = (e - s).Days + 1;
        if (days != AnalysisRangeDays)
            throw new BusinessException($"宝宝喂养分析仅支持连续{AnalysisRangeDays}天数据");
        // 解析得到的 DateTime.Kind 为 Unspecified，需指定为 UTC 后才能写 timestamptz 列
        return (DateTime.SpecifyKind(s.Date, DateTimeKind.Utc), DateTime.SpecifyKind(e.Date, DateTimeKind.Utc));
    }

    private async Task<Baby> ResolveBabyAsync(string userId, string? babyId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(babyId))
        {
            await _babyAccess.EnsureAccessAsync(userId, babyId, ct);
            return await _db.Babies.FirstOrDefaultAsync(b => b.Id == babyId, ct)
                ?? throw new NotFoundException("宝宝不存在");
        }
        // 默认取第一个有访问权的宝宝
        return await _babyAccess.GetDefaultBabyAsync(userId, ct)
            ?? throw new NotFoundException("未找到宝宝");
    }

    private async Task<string?> ResolveBabyIdForQueryAsync(string userId, string? babyId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(babyId)) return null;
        await _babyAccess.EnsureAccessAsync(userId, babyId, ct);
        return babyId;
    }

    private static string BuildSourceText(Baby baby, DateTime start, DateTime end, List<ChildRecord> records)
    {
        var sb = new StringBuilder();
        var recordDays = records.Select(r => r.RecordDate).Distinct().Count();
        var ageDays = BabyUtil.GetAgeInDays(baby.BirthDate);

        sb.AppendLine("一、宝宝信息");
        sb.AppendLine($"姓名：{baby.Name}");
        sb.AppendLine($"性别：{baby.Gender}");
        sb.AppendLine($"出生日期：{baby.BirthDate:yyyy-MM-dd}");
        sb.AppendLine($"当前年龄：{ageDays}天");
        sb.AppendLine($"分析区间：{start:yyyy-MM-dd} 至 {end:yyyy-MM-dd}");
        sb.AppendLine($"有记录天数：{recordDays}");
        sb.AppendLine($"记录总数：{records.Count}");

        // 数据完整度提示
        if (recordDays < 4)
        {
            var tip = recordDays == 0
                ? "数据完整度提示: 当前区间没有任何记录，请补充数据后再次生成分析。"
                : $"数据完整度提示: 当前区间仅有 {recordDays} 天数据，建议连续记录后再分析。";
            sb.AppendLine(tip);
        }

        sb.AppendLine();
        sb.AppendLine("二、汇总统计");
        var feedCount = records.Count(r => r.RecordType == RecordType.Feed);
        var bottleMl = records.Where(r => r.RecordType == RecordType.Feed && r.AmountMl.HasValue).Sum(r => r.AmountMl ?? 0);
        var breastDurationSec = records.Where(r => r.RecordType == RecordType.Feed).Sum(r => r.DurationSec ?? 0);
        var sleepCount = records.Count(r => r.RecordType == RecordType.Sleep);
        var sleepDurationSec = records.Where(r => r.RecordType == RecordType.Sleep).Sum(r => r.DurationSec ?? 0);
        var diaperCount = records.Count(r => r.RecordType == RecordType.Diaper);
        var abnormalCount = records.Count(r => r.AbnormalFlag == true);
        var maxTemp = records.Where(r => r.TemperatureValue.HasValue).Select(r => r.TemperatureValue).Max();
        var latestHeight = records.Where(r => r.HeightCm.HasValue).OrderByDescending(r => r.RecordTime).Select(r => r.HeightCm).FirstOrDefault();
        var latestWeight = records.Where(r => r.WeightKg.HasValue).OrderByDescending(r => r.RecordTime).Select(r => r.WeightKg).FirstOrDefault();
        var pumpCount = records.Count(r => r.RecordType == RecordType.Pump);
        var activityCount = records.Count(r => r.RecordType == RecordType.Activity);

        sb.AppendLine($"喂养次数：{feedCount}");
        sb.AppendLine($"奶瓶奶量(ml)：{bottleMl}");
        sb.AppendLine($"亲喂时长(秒)：{breastDurationSec}");
        sb.AppendLine($"睡眠次数：{sleepCount}");
        sb.AppendLine($"睡眠时长(秒)：{sleepDurationSec}");
        sb.AppendLine($"尿布次数：{diaperCount}");
        sb.AppendLine($"异常记录数：{abnormalCount}");
        sb.AppendLine($"最高体温：{maxTemp}");
        sb.AppendLine($"最新身高(cm)：{latestHeight}");
        sb.AppendLine($"最新体重(kg)：{latestWeight}");
        sb.AppendLine($"吸奶次数：{pumpCount}");
        sb.AppendLine($"活动次数：{activityCount}");

        sb.AppendLine();
        sb.AppendLine("三、记录明细");
        foreach (var r in records)
        {
            var time = r.RecordTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            var typeInfo = string.IsNullOrEmpty(r.RecordSubType)
                ? r.RecordType : $"{r.RecordType}({r.RecordSubType})";
            var detail = new List<string>();
            if (r.AmountMl.HasValue) detail.Add($"量={r.AmountMl}ml");
            if (r.DurationSec.HasValue) detail.Add($"时长={r.DurationSec}s");
            if (r.TemperatureValue.HasValue) detail.Add($"体温={r.TemperatureValue}");
            if (r.HeightCm.HasValue) detail.Add($"身高={r.HeightCm}cm");
            if (r.WeightKg.HasValue) detail.Add($"体重={r.WeightKg}kg");
            if (r.AbnormalFlag == true) detail.Add("异常");
            sb.AppendLine($"- {time} | {typeInfo} | {string.Join(" | ", detail)}");
            if (sb.Length > MaxSourceTextLength)
            {
                sb.AppendLine("[后端提示：原始记录较多，后续明细已截断...]");
                break;
            }
        }
        return sb.ToString();
    }

    private static AiAnalysisRecordDto ToDto(AiAnalysisRecord r)
    {
        var tip = "";
        var idx = r.SourceText.IndexOf("数据完整度提示:");
        if (idx >= 0)
        {
            var end = r.SourceText.IndexOf('\n', idx);
            tip = end > idx ? r.SourceText.Substring(idx, end - idx).Trim() : r.SourceText[idx..].Trim();
        }
        return new AiAnalysisRecordDto
        {
            Id = r.Id,
            BabyId = r.BabyId,
            BabyName = r.BabyName,
            RangeStartDate = r.RangeStartDate.ToString("yyyy-MM-dd"),
            RangeEndDate = r.RangeEndDate.ToString("yyyy-MM-dd"),
            AnalysisText = r.AnalysisText,
            DataQualityTip = tip,
            Model = r.Model,
            CreatedAt = DateTimeFormatter.FormatDateTimeMinute(r.CreatedAt),
            UpdatedAt = DateTimeFormatter.FormatDateTimeMinute(r.UpdatedAt),
        };
    }

    private static string LoadSkillPrompt()
    {
        // 技能提示词：阶段 2 用简化版，后续可从文件加载
        return """
你是一位专业的婴幼儿喂养与成长分析助手。请基于后端整理的连续7天记录 TXT，输出结构化的分析和建议。

输出要求：
1. 用中文输出，使用 Markdown 格式。
2. 分析维度包括：喂养情况、睡眠情况、排泄情况、体温与健康、生长发育、异常提示。
3. 每个维度先总结数据，再给出针对性建议。
4. 末尾给出"综合建议"，不超过 5 条具体可执行的建议。
5. 语气专业、温和、鼓励，避免制造焦虑。
6. 不要编造 TXT 中没有的数据；若数据不足，明确指出。
""";
    }
}
