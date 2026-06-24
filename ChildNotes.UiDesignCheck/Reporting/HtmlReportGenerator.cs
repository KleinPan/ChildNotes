using System.Text;
using System.Text.Json;

namespace ChildNotes.UiDesignCheck.Reporting;

public static class HtmlReportGenerator
{
    public static void Generate(ComplianceReport report, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang='zh-CN'><head><meta charset='utf-8'>");
        sb.AppendLine("<meta name='viewport' content='width=device-width,initial-scale=1'>");
        sb.AppendLine("<title>UI 设计规范合规性报告</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(":root{--err:#e74c3c;--warn:#e67e22;--info:#3498db;--ok:#27ae60;--bg:#f5f6f8;--card:#fff;--ink:#1a1a1a;--muted:#888}");
        sb.AppendLine("*{box-sizing:border-box}body{margin:0;font-family:'Segoe UI',system-ui,sans-serif;background:var(--bg);color:var(--ink);line-height:1.6}");
        sb.AppendLine("header{background:linear-gradient(135deg,#07C160,#10AEFF);color:#fff;padding:32px 24px}");
        sb.AppendLine("header h1{margin:0 0 8px;font-size:24px}header p{margin:0;opacity:.9}");
        sb.AppendLine(".wrap{max-width:1200px;margin:0 auto;padding:24px}");
        sb.AppendLine(".summary{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:16px;margin-bottom:24px}");
        sb.AppendLine(".stat{background:var(--card);border-radius:12px;padding:20px;box-shadow:0 1px 4px rgba(0,0,0,.06)}");
        sb.AppendLine(".stat .n{font-size:32px;font-weight:700}.stat .l{color:var(--muted);font-size:13px;margin-top:4px}");
        sb.AppendLine(".screen{background:var(--card);border-radius:12px;margin-bottom:24px;overflow:hidden;box-shadow:0 1px 4px rgba(0,0,0,.06)}");
        sb.AppendLine(".screen-h{padding:16px 20px;border-bottom:1px solid #eee;display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px}");
        sb.AppendLine(".screen-h h2{margin:0;font-size:18px}.score{font-weight:700;font-size:20px}");
        sb.AppendLine(".score.good{color:var(--ok)}.score.mid{color:var(--warn)}.score.bad{color:var(--err)}");
        sb.AppendLine(".imgs{display:flex;gap:16px;padding:20px;flex-wrap:wrap;background:#fafafa}");
        sb.AppendLine(".imgs figure{margin:0}.imgs img{width:200px;border:1px solid #ddd;border-radius:8px}.imgs figcaption{text-align:center;font-size:12px;color:var(--muted);margin-top:6px}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;font-size:13px}");
        sb.AppendLine("th,td{text-align:left;padding:10px 12px;border-bottom:1px solid #f0f0f0;vertical-align:top}");
        sb.AppendLine("th{background:#fafafa;font-weight:600;white-space:nowrap}");
        sb.AppendLine(".sev{display:inline-block;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:600;color:#fff}");
        sb.AppendLine(".sev.Error{background:var(--err)}.sev.Warn{background:var(--warn)}.sev.Info{background:var(--info)}");
        sb.AppendLine(".cat{display:inline-block;padding:2px 8px;border-radius:10px;font-size:11px;background:#eef;color:#556}");
        sb.AppendLine("tr:hover{background:#f9f9f9}.sug{color:#444}.dev{font-family:monospace;font-size:12px;color:var(--err)}");
        sb.AppendLine(".ai-block{background:#f0fff0;border:1px solid #b7e4b7;border-radius:8px;padding:16px;margin:16px 20px;font-size:13px;white-space:pre-wrap;font-family:monospace}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<header><h1>UI 设计规范合规性报告</h1>");
        sb.AppendLine($"<p>规范: {Escape(report.SpecName)} · 生成时间: {report.GeneratedAt} · 总体合规分: {report.OverallScore:F1}%</p></header>");
        sb.AppendLine("<div class='wrap'>");

        sb.AppendLine("<div class='summary'>");
        sb.AppendLine(Stat(report.TotalScreens, "检测界面数"));
        sb.AppendLine(Stat(report.TotalViolations, "违规项总数"));
        sb.AppendLine(Stat(report.Screens.Sum(s => s.ErrorCount), "严重错误"));
        sb.AppendLine(Stat(report.Screens.Sum(s => s.WarnCount), "警告"));
        sb.AppendLine(Stat(report.OverallScore.ToString("F1") + "%", "总体合规分"));
        sb.AppendLine("</div>");

        foreach (var s in report.Screens)
        {
            var scoreClass = s.ComplianceScore >= 90 ? "good" : s.ComplianceScore >= 70 ? "mid" : "bad";
            sb.AppendLine("<div class='screen'>");
            sb.AppendLine("<div class='screen-h'>");
            sb.AppendLine($"<h2>{Escape(s.ScreenName)}</h2>");
            sb.AppendLine($"<div>元素 {s.TotalElements} · 违规 {s.ViolationCount} · <span class='score {scoreClass}'>{s.ComplianceScore:F1}%</span></div>");
            sb.AppendLine("</div>");

            if (!string.IsNullOrEmpty(s.ScreenshotPath) || !string.IsNullOrEmpty(s.AnnotatedPath))
            {
                sb.AppendLine("<div class='imgs'>");
                if (!string.IsNullOrEmpty(s.ScreenshotPath) && File.Exists(s.ScreenshotPath))
                    sb.AppendLine($"<figure><img src='{Escape(ToRelative(outputPath, s.ScreenshotPath))}'><figcaption>原始截图</figcaption></figure>");
                if (!string.IsNullOrEmpty(s.AnnotatedPath) && File.Exists(s.AnnotatedPath))
                    sb.AppendLine($"<figure><img src='{Escape(ToRelative(outputPath, s.AnnotatedPath))}'><figcaption>标注对比</figcaption></figure>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("<div class='ai-block'>");
            sb.AppendLine(BuildAiGuidance(s));
            sb.AppendLine("</div>");

            sb.AppendLine("<table><thead><tr><th>级别</th><th>类别</th><th>元素</th><th>位置</th><th>规则</th><th>期望</th><th>实际</th><th>偏差</th><th>修改建议</th></tr></thead><tbody>");
            foreach (var v in s.Violations.OrderByDescending(x => x.Severity))
            {
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td><span class='sev {v.Severity}'>{v.Severity}</span></td>");
                sb.AppendLine($"<td><span class='cat'>{Escape(v.Category)}</span></td>");
                sb.AppendLine($"<td>{Escape(v.Element)}</td>");
                sb.AppendLine($"<td>{Escape(v.Location)}</td>");
                sb.AppendLine($"<td>{Escape(v.Rule)}</td>");
                sb.AppendLine($"<td>{Escape(v.Expected)}</td>");
                sb.AppendLine($"<td>{Escape(v.Actual)}</td>");
                sb.AppendLine($"<td class='dev'>{Escape(v.Deviation)}</td>");
                sb.AppendLine($"<td class='sug'>{Escape(v.Suggestion)}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table></div>");
        }

        sb.AppendLine("</div></body></html>");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    private static string BuildAiGuidance(ScreenReport s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {s.ScreenName} 自动化样式调整指令 (供 AI 系统使用)");
        sb.AppendLine($"合规分: {s.ComplianceScore:F1}% | 违规: {s.ViolationCount} (错误 {s.ErrorCount}/警告 {s.WarnCount}/提示 {s.InfoCount})");
        sb.AppendLine("以下为按优先级排序的修改项:");
        var grouped = s.Violations
            .OrderByDescending(v => v.Severity)
            .GroupBy(v => v.Category);
        var idx = 1;
        foreach (var g in grouped)
        {
            sb.AppendLine($"  [{g.Key}] 共 {g.Count()} 项:");
            foreach (var v in g.Take(8))
            {
                sb.AppendLine($"    {idx++}. {v.Element} @ {v.Location}");
                sb.AppendLine($"       规则: {v.Rule} | 期望: {v.Expected} | 实际: {v.Actual} | 偏差: {v.Deviation}");
                sb.AppendLine($"       建议: {v.Suggestion}");
            }
            if (g.Count() > 8) sb.AppendLine($"    ... 及其余 {g.Count() - 8} 项");
        }
        return sb.ToString();
    }

    private static string Stat(object n, string label) =>
        $"<div class='stat'><div class='n'>{Escape(n.ToString() ?? "")}</div><div class='l'>{Escape(label)}</div></div>";

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string ToRelative(string fromPath, string toPath)
    {
        try
        {
            var fromDir = Path.GetDirectoryName(fromPath) ?? "";
            var uri = new Uri(Path.Combine(fromDir, toPath));
            var baseUri = new Uri(fromPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(uri).ToString());
        }
        catch { return toPath; }
    }
}
