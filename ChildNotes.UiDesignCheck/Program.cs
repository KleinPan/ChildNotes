using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Threading;
using ChildNotes.UiDesignCheck.Analysis;
using ChildNotes.UiDesignCheck.Capture;
using ChildNotes.UiDesignCheck.Reporting;
using ChildNotes.UiDesignCheck.Spec;

namespace ChildNotes.UiDesignCheck;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<int> Main(string[] args)
    {
        var specPath = args.Length > 0 ? args[0] : "design-spec.json";
        var outDir = args.Length > 1 ? args[1] : "ui-check-reports";

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== UI 设计规范自动化检查工具 ===");
        Console.WriteLine($"规范文件: {specPath}");
        Console.WriteLine($"输出目录: {outDir}");
        Console.WriteLine();

        var spec = LoadSpec(specPath);
        Directory.CreateDirectory(outDir);

        var screenFactories = new (string Name, Func<Control> Factory)[]
        {
            ("Login", ScreenCapturer.CreateLoginScreen),
            ("Home", ScreenCapturer.CreateHomeScreen),
            ("Feeding", ScreenCapturer.CreateFeedingScreen),
            ("Growth", ScreenCapturer.CreateGrowthScreen),
            ("Mine", ScreenCapturer.CreateMineScreen),
            ("Statistics", ScreenCapturer.CreateStatisticsScreen),
            ("AiAnalysis", ScreenCapturer.CreateAiAnalysisScreen),
            ("Family", ScreenCapturer.CreateFamilyScreen),
            ("Points", ScreenCapturer.CreatePointsScreen),
            ("BabySetup", ScreenCapturer.CreateBabySetupScreen),
            ("MainShell", ScreenCapturer.CreateMainShellScreen),
        };

        var compliance = new ComplianceReport
        {
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            SpecName = spec.Name,
        };

        Console.WriteLine("[初始化] 启动 Avalonia 无头渲染引擎 ...");
        HeadlessBootstrap.Start();
        Console.WriteLine("[初始化] 完成");
        Console.WriteLine();

        var checker = new SpecChecker(spec);

        foreach (var (name, factory) in screenFactories)
        {
            Console.WriteLine($"[捕获] {name} ...");
            try
            {
                var (captured, screenshotPath) = HeadlessBootstrap.InvokeOnUi(() =>
                {
                    var control = factory();
                    var cap = ScreenCapturer.Capture(control, name, spec.ViewportWidth, spec.ViewportHeight);
                    var path = Path.Combine(outDir, $"{name}.png");
                    if (cap.Bitmap is not null)
                    {
                        using var fs = File.OpenWrite(path);
                        cap.Bitmap.Save(fs);
                    }
                    return (cap, path);
                });

                if (captured.Bitmap is null)
                {
                    Console.WriteLine($"  [跳过] {name} 未能渲染位图(请确认已启用 Skia 且 UseHeadlessDrawing=false)");
                    continue;
                }

                Console.WriteLine($"  [分析] 可视化树 + 像素采样 ...");
                var violations = HeadlessBootstrap.InvokeOnUi(() =>
                {
                    var bmp = captured.Bitmap!;
                    return checker.Check(captured.VisualTree, rect => PixelSampler.SampleRegionAverage(bmp, rect));
                });

                var totalElements = VisualTreeExtractor.Flatten(captured.VisualTree)
                    .Count(e => e.IsVisible && e.Width > 0 && e.Height > 0);

                var screenReport = BuildScreenReport(name, screenshotPath, totalElements, violations);
                compliance.Screens.Add(screenReport);

                var annotatedPath = Path.Combine(outDir, $"{name}_annotated.png");
                try
                {
                    OverlayRenderer.Render(screenshotPath, annotatedPath, screenReport);
                    screenReport.AnnotatedPath = annotatedPath;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [警告] 标注图生成失败: {ex.Message}");
                }

                Console.WriteLine($"  [完成] {name}: 合规分 {screenReport.ComplianceScore:F1}% | 违规 {screenReport.ViolationCount} (E{screenReport.ErrorCount}/W{screenReport.WarnCount}/I{screenReport.InfoCount})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [错误] {name} 捕获/分析失败: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        HeadlessBootstrap.Shutdown();

        compliance.TotalScreens = compliance.Screens.Count;
        compliance.TotalViolations = compliance.Screens.Sum(s => s.ViolationCount);
        compliance.OverallScore = compliance.Screens.Count > 0
            ? compliance.Screens.Average(s => s.ComplianceScore)
            : 0;

        var jsonPath = Path.Combine(outDir, "report.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(compliance, JsonOpts));

        var htmlPath = Path.Combine(outDir, "report.html");
        HtmlReportGenerator.Generate(compliance, htmlPath);

        Console.WriteLine();
        Console.WriteLine("=== 检查完成 ===");
        Console.WriteLine($"总体合规分: {compliance.OverallScore:F1}%");
        Console.WriteLine($"总违规项: {compliance.TotalViolations}");
        Console.WriteLine($"JSON 报告: {Path.GetFullPath(jsonPath)}");
        Console.WriteLine($"HTML 报告: {Path.GetFullPath(htmlPath)}");
        Console.WriteLine($"截图目录: {Path.GetFullPath(outDir)}");

        return compliance.Screens.Sum(s => s.ErrorCount) > 0 ? 2 : 0;
    }

    private static ScreenReport BuildScreenReport(string name, string screenshotPath, int totalElements, List<Violation> violations)
    {
        var errorCount = violations.Count(v => v.Severity == Severity.Error);
        var warnCount = violations.Count(v => v.Severity == Severity.Warn);
        var infoCount = violations.Count(v => v.Severity == Severity.Info);

        var penalty = errorCount * 10.0 + warnCount * 3.0 + infoCount * 0.5;
        var score = Math.Max(0, 100 - penalty);

        return new ScreenReport
        {
            ScreenName = name,
            ScreenshotPath = screenshotPath,
            TotalElements = totalElements,
            Violations = violations,
            ViolationCount = violations.Count,
            ErrorCount = errorCount,
            WarnCount = warnCount,
            InfoCount = infoCount,
            ComplianceScore = score,
        };
    }

    private static DesignSpec LoadSpec(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"规范文件不存在: {path}，使用内置默认规范。");
            return new DesignSpec();
        }
        var json = File.ReadAllText(path);
        var spec = JsonSerializer.Deserialize<DesignSpec>(json, JsonOpts) ?? new DesignSpec();
        Console.WriteLine($"已加载规范: {spec.Name}");
        return spec;
    }
}
