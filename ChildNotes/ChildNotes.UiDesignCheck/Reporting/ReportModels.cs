using Avalonia;

namespace ChildNotes.UiDesignCheck.Reporting;

public enum Severity { Error, Warn, Info }

public sealed class Violation
{
    public Severity Severity { get; set; }
    public string Category { get; set; } = "";
    public string Element { get; set; } = "";
    public string Location { get; set; } = "";
    public string Rule { get; set; } = "";
    public string Expected { get; set; } = "";
    public string Actual { get; set; } = "";
    public string Deviation { get; set; } = "";
    public string Suggestion { get; set; } = "";
}

public sealed class ScreenReport
{
    public string ScreenName { get; set; } = "";
    public string ScreenshotPath { get; set; } = "";
    public string AnnotatedPath { get; set; } = "";
    public int TotalElements { get; set; }
    public int ViolationCount { get; set; }
    public int ErrorCount { get; set; }
    public int WarnCount { get; set; }
    public int InfoCount { get; set; }
    public double ComplianceScore { get; set; }
    public List<Violation> Violations { get; set; } = new();
}

public sealed class ComplianceReport
{
    public string GeneratedAt { get; set; } = "";
    public string SpecName { get; set; } = "";
    public int TotalScreens { get; set; }
    public int TotalViolations { get; set; }
    public double OverallScore { get; set; }
    public List<ScreenReport> Screens { get; set; } = new();
}
