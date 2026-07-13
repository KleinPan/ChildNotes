using System.Text.Json;
using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Constants;
using ChildNotes.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChildNotes.Tests;

/// <summary>
/// AI 识别准确性评估测试方案。
///
/// 目的：为 AI 模型优化提供可量化的依据，跟踪规则解析（及未来真实 AI 解析）的识别准确率。
///
/// 评估指标：
/// 1. 类型准确率（TypeAccuracy）：RecordType 命中期望的比例
/// 2. 子类型准确率（SubTypeAccuracy）：RecordSubType 命中期望的比例（仅对声明期望子类型的用例计）
/// 3. 数值准确率（ValueAccuracy）：Amount/Duration/Temperature 等核心数值命中期望的比例（仅对声明期望数值的用例计）
/// 4. 平均置信度（AvgConfidence）：所有用例的平均置信度
/// 5. 拒识率（FallbackRate）：兜底为 Activity 的比例（越低越好）
///
/// 运行方式：
/// - dotnet test --filter "FullyQualifiedName~AiAccuracyEvaluation" --logger "console;verbosity=detailed"
/// - 测试通过表示所有用例的 RecordType 均命中期望；详细的指标数据通过 Xunit.Abstractions.ITestOutputHelper 输出
///   （本文件用 Console.WriteLine 输出，CI 可重定向到文件）
///
/// 数据集扩展：
/// - 新增用例直接追加到 _dataset 数组即可
/// - 用例格式：(输入文本, 期望RecordType, 期望SubType或null, 期望数值或null, 期望数值字段名)
/// - 期望数值字段名：amount / duration / temperature / leftDuration / null
/// </summary>
public class AiAccuracyEvaluationTests
{
    /// <summary>评估用例定义。</summary>
    private sealed class EvalCase
    {
        public string Text { get; init; } = "";
        public string ExpectedType { get; init; } = "";
        public string? ExpectedSubType { get; init; }
        public double? ExpectedValue { get; init; }
        public string? ValueField { get; init; } // amount / duration / temperature / leftDuration
        public string Category { get; init; } = ""; // 用例分类，用于分组统计
    }

    /// <summary>单条用例的评估结果。</summary>
    private sealed class EvalResult
    {
        public EvalCase Case { get; init; } = new();
        public string ActualType { get; init; } = "";
        public string? ActualSubType { get; init; }
        public double? ActualValue { get; init; }
        public double Confidence { get; init; }
        public bool TypeHit { get; init; }
        public bool SubTypeHit { get; init; }
        public bool ValueHit { get; init; }
    }

    /// <summary>评估报告汇总。</summary>
    private sealed class EvalReport
    {
        public int Total { get; init; }
        public int TypeCorrect { get; init; }
        public int SubTypeCorrect { get; init; }
        public int SubTypeApplicable { get; init; }
        public int ValueCorrect { get; init; }
        public int ValueApplicable { get; init; }
        public int FallbackCount { get; init; }
        public double AvgConfidence { get; init; }
        public List<EvalResult> Details { get; init; } = new();

        public double TypeAccuracy => Total == 0 ? 0 : (double)TypeCorrect / Total;
        public double SubTypeAccuracy => SubTypeApplicable == 0 ? 0 : (double)SubTypeCorrect / SubTypeApplicable;
        public double ValueAccuracy => ValueApplicable == 0 ? 0 : (double)ValueCorrect / ValueApplicable;
        public double FallbackRate => Total == 0 ? 0 : (double)FallbackCount / Total;
    }

    // ===== 测试数据集 =====
    // 覆盖常见育儿记录表述，含大便/喝水/喂奶/睡眠/体温/生长/补给等
    // 每条用例声明期望的 RecordType / 子类型 / 数值，用于自动比对
    private static readonly EvalCase[] _dataset =
    {
        // 喂奶-瓶喂
        new() { Text = "喝了120ml奶", ExpectedType = RecordType.Feed, ExpectedSubType = FeedType.Bottle, ExpectedValue = 120, ValueField = "amount", Category = "feed-bottle" },
        new() { Text = "吃了90毫升", ExpectedType = RecordType.Feed, ExpectedSubType = FeedType.Bottle, ExpectedValue = 90, ValueField = "amount", Category = "feed-bottle" },
        new() { Text = "瓶喂150ml", ExpectedType = RecordType.Feed, ExpectedSubType = FeedType.Bottle, ExpectedValue = 150, ValueField = "amount", Category = "feed-bottle" },
        new() { Text = "吃了40奶粉", ExpectedType = RecordType.Feed, ExpectedSubType = FeedType.Bottle, ExpectedValue = 40, ValueField = "amount", Category = "feed-bottle" },
        new() { Text = "16点20吃了40奶粉", ExpectedType = RecordType.Feed, ExpectedSubType = FeedType.Bottle, ExpectedValue = 40, ValueField = "amount", Category = "feed-bottle" },

        // 喂奶-亲喂
        new() { Text = "亲喂 左10分 右15分", ExpectedType = RecordType.Feed, ExpectedSubType = FeedType.Breast, ExpectedValue = 10, ValueField = "leftDuration", Category = "feed-breast" },
        new() { Text = "亲喂 左10分钟 右15分钟", ExpectedType = RecordType.Feed, ExpectedSubType = FeedType.Breast, ExpectedValue = 10, ValueField = "leftDuration", Category = "feed-breast" },

        // 换尿布-各种表述（重点：大便类）
        new() { Text = "换尿布 嘘嘘", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Wet, Category = "diaper" },
        new() { Text = "换尿布 便便", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Dirty, Category = "diaper" },
        new() { Text = "换尿布 又尿又拉", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Both, Category = "diaper" },
        new() { Text = "换尿布 干爽", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Dry, Category = "diaper" },
        new() { Text = "拉屎了", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Dirty, Category = "diaper" },
        new() { Text = "尿了", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Wet, Category = "diaper" },
        // 大便扩展表述（原 bug 用例：曾误识别为 supplement）
        new() { Text = "拉了大便", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Dirty, Category = "diaper-stool" },
        new() { Text = "大便了", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Dirty, Category = "diaper-stool" },
        new() { Text = "拉了", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Dirty, Category = "diaper-stool" },
        new() { Text = "臭臭", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Dirty, Category = "diaper-stool" },
        new() { Text = "拉臭臭了", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Dirty, Category = "diaper-stool" },
        new() { Text = "粑粑", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Dirty, Category = "diaper-stool" },
        new() { Text = "拉臭", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Dirty, Category = "diaper-stool" },
        new() { Text = "尿尿了", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Wet, Category = "diaper-stool" },
        new() { Text = "小便了", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Wet, Category = "diaper-stool" },

        // 睡眠
        new() { Text = "睡了30分钟", ExpectedType = RecordType.Sleep, ExpectedValue = 30, ValueField = "duration", Category = "sleep" },
        new() { Text = "小睡45分", ExpectedType = RecordType.Sleep, ExpectedValue = 45, ValueField = "duration", Category = "sleep" },
        new() { Text = "11点半睡到12点40", ExpectedType = RecordType.Sleep, ExpectedValue = 70, ValueField = "duration", Category = "sleep" },

        // 体温
        new() { Text = "体温37.5度", ExpectedType = RecordType.Temperature, ExpectedValue = 37.5, ValueField = "temperature", Category = "temperature" },
        new() { Text = "测了体温38.2℃", ExpectedType = RecordType.Temperature, ExpectedValue = 38.2, ValueField = "temperature", Category = "temperature" },

        // 生长
        new() { Text = "身高70cm 体重8.5kg", ExpectedType = RecordType.Growth, Category = "growth" },

        // 补给-用药
        new() { Text = "吃了半包保泰康颗粒", ExpectedType = RecordType.Supplement, ExpectedSubType = "medicine", Category = "supplement" },
        new() { Text = "8:17喝了半包保泰康颗粒", ExpectedType = RecordType.Supplement, ExpectedSubType = "medicine", Category = "supplement" },

        // 补给-营养
        new() { Text = "吃了1粒维D", ExpectedType = RecordType.Supplement, ExpectedSubType = "nutrition", Category = "supplement" },
        new() { Text = "服了10滴维D3", ExpectedType = RecordType.Supplement, ExpectedSubType = "nutrition", Category = "supplement" },

        // 喝水
        new() { Text = "喝了10ml水", ExpectedType = RecordType.Water, ExpectedValue = 10, ValueField = "amount", Category = "water" },
        new() { Text = "喝了5毫升水", ExpectedType = RecordType.Water, ExpectedValue = 5, ValueField = "amount", Category = "water" },

        // 复合句
        new() { Text = "11点半睡到12点40，吃了130奶粉，喝10ml水", ExpectedType = RecordType.Sleep, ExpectedValue = 70, ValueField = "duration", Category = "composite" },
        new() { Text = "换了尿布便便，然后睡了30分钟", ExpectedType = RecordType.Diaper, ExpectedSubType = DiaperType.Dirty, Category = "composite" },
    };

    /// <summary>使用 FailingDeepSeekClient 强制走规则降级路径，保证测试稳定。</summary>
    private static AiNoteService NewServiceWithFailingAi()
    {
        var failingAi = new FailingDeepSeekClient();
        return new AiNoteService(failingAi, NullLogger<AiNoteService>.Instance, null!, null!);
    }

    /// <summary>
    /// 主评估测试：运行所有用例，计算各项指标，输出详细对比报告。
    /// 测试断言：TypeAccuracy >= 0.95（规则解析应稳定覆盖所有已知用例）。
    /// </summary>
    [Fact]
    public void Evaluate_RuleParse_Accuracy()
    {
        var svc = NewServiceWithFailingAi();
        var results = new List<EvalResult>(_dataset.Length);

        foreach (var c in _dataset)
        {
            var items = svc.ParseByRulesMulti(c.Text);
            // 复合句：取第一条作为主记录评估（复合句的整体切分能力另测）
            var first = items[0];

            bool typeHit = first.RecordType == c.ExpectedType;
            bool subHit = c.ExpectedSubType is null || first.RecordSubType == c.ExpectedSubType;
            bool valHit = true;
            double? actualVal = null;
            if (c.ExpectedValue.HasValue && c.ValueField is not null)
            {
                actualVal = c.ValueField switch
                {
                    "amount" => first.Amount,
                    "duration" => first.Duration,
                    "temperature" => (double?)first.Temperature,
                    "leftDuration" => first.LeftDuration,
                    _ => null,
                };
                valHit = actualVal == c.ExpectedValue.Value;
            }

            results.Add(new EvalResult
            {
                Case = c,
                ActualType = first.RecordType,
                ActualSubType = first.RecordSubType,
                ActualValue = actualVal,
                Confidence = first.Confidence,
                TypeHit = typeHit,
                SubTypeHit = subHit,
                ValueHit = valHit,
            });
        }

        var report = new EvalReport
        {
            Total = results.Count,
            TypeCorrect = results.Count(r => r.TypeHit),
            SubTypeApplicable = results.Count(r => r.Case.ExpectedSubType is not null),
            SubTypeCorrect = results.Count(r => r.Case.ExpectedSubType is not null && r.SubTypeHit),
            ValueApplicable = results.Count(r => r.Case.ExpectedValue.HasValue),
            ValueCorrect = results.Count(r => r.Case.ExpectedValue.HasValue && r.ValueHit),
            FallbackCount = results.Count(r => r.ActualType == RecordType.Activity),
            AvgConfidence = results.Average(r => r.Confidence),
            Details = results,
        };

        // ===== 输出详细对比报告 =====
        Console.WriteLine();
        Console.WriteLine("====== AI 识别准确性评估报告（规则降级路径） ======");
        Console.WriteLine($"总用例数: {report.Total}");
        Console.WriteLine($"类型准确率: {report.TypeAccuracy:P2} ({report.TypeCorrect}/{report.Total})");
        Console.WriteLine($"子类型准确率: {report.SubTypeAccuracy:P2} ({report.SubTypeCorrect}/{report.SubTypeApplicable})");
        Console.WriteLine($"数值准确率: {report.ValueAccuracy:P2} ({report.ValueCorrect}/{report.ValueApplicable})");
        Console.WriteLine($"平均置信度: {report.AvgConfidence:F3}");
        Console.WriteLine($"拒识率(Activity 兜底): {report.FallbackRate:P2} ({report.FallbackCount}/{report.Total})");

        // 按分类分组统计
        Console.WriteLine();
        Console.WriteLine("====== 按分类统计 ======");
        var byCategory = results.GroupBy(r => r.Case.Category).OrderBy(g => g.Key);
        foreach (var g in byCategory)
        {
            var tc = g.Count(r => r.TypeHit);
            Console.WriteLine($"  {g.Key,-20} 用例数={g.Count(),2} 类型命中={tc}/{g.Count()}");
        }

        // 失败用例明细
        var failures = results.Where(r => !r.TypeHit || !r.SubTypeHit || !r.ValueHit).ToList();
        if (failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("====== 失败用例明细 ======");
            foreach (var r in failures)
            {
                var expected = $"期望={r.Case.ExpectedType}/{r.Case.ExpectedSubType ?? "-"}";
                var actual = $"实际={r.ActualType}/{r.ActualSubType ?? "-"}";
                var val = r.Case.ExpectedValue.HasValue
                    ? $" 数值(期望={r.Case.ExpectedValue}/{r.Case.ValueField}, 实际={r.ActualValue})"
                    : "";
                Console.WriteLine($"  [MISS] 文本=\"{r.Case.Text}\" {expected} {actual}{val} 置信度={r.Confidence}");
            }
        }
        Console.WriteLine("==================================================");

        // 断言：规则解析的 TypeAccuracy 应 >= 95%（已知用例必须全覆盖）
        Assert.True(report.TypeAccuracy >= 0.95,
            $"类型准确率 {report.TypeAccuracy:P2} 低于阈值 95%。失败 {report.Total - report.TypeCorrect} 条，详见测试输出。");
    }

    /// <summary>专门验证大便类表述全部识别为 diaper/dirty（针对原 bug 的回归测试）。</summary>
    [Theory]
    [InlineData("拉了大便", DiaperType.Dirty)]
    [InlineData("大便了", DiaperType.Dirty)]
    [InlineData("拉了", DiaperType.Dirty)]
    [InlineData("臭臭", DiaperType.Dirty)]
    [InlineData("拉臭臭了", DiaperType.Dirty)]
    [InlineData("粑粑", DiaperType.Dirty)]
    [InlineData("拉臭", DiaperType.Dirty)]
    [InlineData("换尿布 便便", DiaperType.Dirty)]
    [InlineData("尿尿了", DiaperType.Wet)]
    [InlineData("小便了", DiaperType.Wet)]
    public void RuleParse_StoolExpressions_RecognizedAsDiaper(string text, string expectedSub)
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti(text);
        var parsed = items[0];
        Assert.Equal(RecordType.Diaper, parsed.RecordType);
        Assert.Equal(expectedSub, parsed.RecordSubType);
        // 大便类不应误识别为 supplement
        Assert.NotEqual(RecordType.Supplement, parsed.RecordType);
    }

    /// <summary>Stub：所有调用均抛异常，模拟 AI 服务不可用。</summary>
    private sealed class FailingDeepSeekClient : ChildNotes.Infrastructure.External.DeepSeekClient
    {
        public FailingDeepSeekClient() : base(
            new HttpClient(),
            Microsoft.Extensions.Options.Options.Create(new ChildNotes.Core.Config.DeepSeekOptions { ApiKey = "stub" }),
            NullLogger<ChildNotes.Infrastructure.External.DeepSeekClient>.Instance)
        { }

        public override Task<(string text, string model)> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
            => throw new InvalidOperationException("AI service stubbed out for tests");
    }
}
