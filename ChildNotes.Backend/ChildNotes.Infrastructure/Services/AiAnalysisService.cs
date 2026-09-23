using System.Text;
using ChildNotes.Core.Common;
using ChildNotes.Core.Config;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Services;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Data;
using ChildNotes.Infrastructure.External;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<AiAnalysisService> _logger;
    private readonly string _skillPrompt;

    public AiAnalysisService(ChildNotesDbContext db, ICurrentUserService current, IBabyAccessService babyAccess, DeepSeekClient ai, PointsWalletService wallet, AiCostOptions cost, IMembershipService membership, ILogger<AiAnalysisService> logger)
    {
        _db = db;
        _current = current;
        _babyAccess = babyAccess;
        _ai = ai;
        _wallet = wallet;
        _cost = cost;
        _membership = membership;
        _logger = logger;
        _skillPrompt = LoadSkillPrompt();
    }

    /// <summary>当前 AI 喂养分析消耗的积分数量（由配置动态控制）。</summary>
    public int AnalysisCostPoints => _cost.AnalysisCost;

    public async Task<AiAnalysisRecordDto> GenerateAsync(GenerateAiAnalysisRequest req, string? babyId, bool usePointsForOverage = false, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var (start, end) = ResolveAnalysisRange(req);

        var baby = await ResolveBabyAsync(uid, babyId, ct);

        var records = await _db.ChildRecords
            .AsNoTracking()
            .Where(r => r.BabyId == baby.Id && r.RecordDate >= start && r.RecordDate <= end)
            .OrderBy(r => r.RecordDate).ThenBy(r => r.RecordTime).ThenBy(r => r.Id)
            .ToListAsync(ct);

        var sourceText = BuildSourceText(baby, start, end, records);

        // 幂等检查：改投影只取幂等比对（SourceText）与 DTO 所需列，避免整行加载 SkillPrompt 大字段
        // （纯 Select(SourceText) 会导致命中/更新路径二次整行加载，净收益为负，故投影列含 DTO 字段）。
        // 投影构造的实体不进 ChangeTracker，后续更新路径再按需加载跟踪实体。
        var existing = await _db.AiAnalysisRecords.AsNoTracking()
            .Where(a => a.UserId == uid && a.BabyId == baby.Id
                && a.RangeStartDate == start && a.RangeEndDate == end)
            .Select(a => new AiAnalysisRecord
            {
                Id = a.Id,
                BabyId = a.BabyId,
                BabyName = a.BabyName,
                RangeStartDate = a.RangeStartDate,
                RangeEndDate = a.RangeEndDate,
                SourceText = a.SourceText,
                AnalysisText = a.AnalysisText,
                Model = a.Model,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
            })
            .FirstOrDefaultAsync(ct);
        // 幂等命中：同区间 + sourceText 相同 → 直接返回（不扣积分、不消耗次数）
        if (existing is not null && existing.SourceText == sourceText)
            return ToDto(existing);

        // 每周次数限制：原子地检查额度并递增（防止并发绕过限制）
        var (ok, used) = await _membership.TryIncrementAiAnalysisUsageAsync(uid, ct);
        // 超限抵扣积分（0 = 非抵扣场景）。AI 调用失败退还时需与正常消耗一并退还。
        var overagePoints = 0;
        if (!ok)
        {
            // limit 仅供超限提示文案使用，懒加载避免与 TryIncrement 内部查询重复（未超限路径不查）
            var limit = await _membership.GetAiAnalysisWeeklyLimitAsync(uid, ct);
            if (!usePointsForOverage)
                throw new BusinessException($"本周 AI 分析次数已用完（{used}/{limit}），升级会员可获得更多次数", 400, "AI_LIMIT_EXCEEDED");

            // 超限积分抵扣：先原子扣抵扣积分（积分不足时 ChangeAsync 抛 INSUFFICIENT_POINTS 直接透传，
            // 此时次数未递增、抵扣积分未扣成功，无需回滚），成功后强制递增次数（允许超限）。
            // 会员/免费用户统一走此逻辑，不做区分。
            overagePoints = MembershipConstants.AiAnalysisOveragePointsCost;
            await _wallet.ChangeAsync(uid, -overagePoints, ct);
            await _membership.ForceIncrementAiAnalysisUsageAsync(uid, ct);
        }

        // 调用 AI 前先扣积分（积分不足抛 BusinessException(INSUFFICIENT_POINTS)）
        // ExecuteUpdateAsync 立即落库，无需事务包裹。
        // 抵扣场景下若正常消耗积分不足：回滚已扣的超限抵扣积分与已递增的次数，
        // 避免出现"扣了抵扣积分却没拿到分析结果"的部分消耗。
        try
        {
            await _wallet.ChangeAsync(uid, -_cost.AnalysisCost, ct);
        }
        catch (BusinessException) when (overagePoints > 0)
        {
            // 正常消耗积分不足回滚：退还已扣的超限抵扣积分 + 回滚已强制递增的次数（best-effort，失败记 Error 不吞）
            try { await _wallet.ChangeAsync(uid, overagePoints, ct); }
            catch (Exception refundEx)
            {
                _logger.LogError(refundEx, "AI 分析积分不足回滚：退还超限抵扣积分失败 userId={Uid} points={Points}",
                    uid, overagePoints);
            }
            try { await _membership.DecrementAiAnalysisUsageAsync(uid, ct); }
            catch (Exception decEx)
            {
                _logger.LogError(decEx, "AI 分析积分不足回滚：回滚已递增次数失败 userId={Uid}", uid);
            }
            throw;
        }

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

            // 次数已在调用前原子递增，无需再单独计数
        }
        catch
        {
            // AI 调用失败：退还超限抵扣积分 + 正常消耗积分（合并为一次原子加回）和已递增的次数
            // （best-effort，失败记 Error 不阻塞异常传播）
            try { await _wallet.ChangeAsync(uid, _cost.AnalysisCost + overagePoints, ct); }
            catch (Exception refundEx)
            {
                _logger.LogError(refundEx, "AI 调用失败补偿退款失败 userId={Uid} points={Points}",
                    uid, _cost.AnalysisCost + overagePoints);
            }
            try { await _membership.DecrementAiAnalysisUsageAsync(uid, ct); }
            catch (Exception decEx)
            {
                _logger.LogError(decEx, "AI 调用失败回滚已递增次数失败 userId={Uid}", uid);
            }
            throw;
        }

        if (existing is not null)
        {
            // 幂等未命中但存在既有记录（数据变化）：投影实体未被跟踪，此处按需加载跟踪实体执行更新
            var tracked = await _db.AiAnalysisRecords.FirstOrDefaultAsync(
                a => a.UserId == uid && a.BabyId == baby.Id
                    && a.RangeStartDate == start && a.RangeEndDate == end, ct);
            if (tracked is not null)
            {
                tracked.BabyName = baby.Name;
                tracked.SourceText = sourceText;
                tracked.SkillPrompt = _skillPrompt;
                tracked.AnalysisText = analysisText;
                tracked.Model = model;
                await _db.SaveChangesAsync(ct);
                return ToDto(tracked);
            }
            // 并发下既有记录已被删除：继续走下方新增路径
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
        // 列表投影：只取 DTO 所需列，跳过 SkillPrompt（列表场景完全未使用）；
        // SourceText 仅用于提取 DataQualityTip（提示行固定位于文本头部约前 400 字符内），
        // 截取前 600 字符避免传输整段大字段（上限 60KB），提取逻辑见 ToDto。
        var list = await _db.AiAnalysisRecords
            .AsNoTracking()
            .Where(a => a.UserId == uid && (targetBabyId == null || a.BabyId == targetBabyId))
            .OrderByDescending(a => a.RangeStartDate)
            .Select(a => new AiAnalysisRecord
            {
                Id = a.Id,
                BabyId = a.BabyId,
                BabyName = a.BabyName,
                RangeStartDate = a.RangeStartDate,
                RangeEndDate = a.RangeEndDate,
                SourceText = a.SourceText.Length < 600 ? a.SourceText : a.SourceText.Substring(0, 600),
                AnalysisText = a.AnalysisText,
                Model = a.Model,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
            })
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
        // 业务日 = 北京时间自然日（#11）：今天按北京墙钟日期取，Kind=Utc 满足
        // Npgsql 写 timestamptz 的要求（避免
        // "Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'"），
        // 与 RecordDate（RecordService 写入用 ChinaTime 墙钟）口径一致。
        var today = DateTime.SpecifyKind(ChinaTime.Today, DateTimeKind.Utc);
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
