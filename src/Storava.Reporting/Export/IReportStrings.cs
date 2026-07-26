namespace Storava.Reporting.Export;

/// <summary>
/// Localized labels for a report. Supplied by the caller so this project stays free of any UI
/// or resource-dictionary dependency, and reports can be produced in either language.
/// </summary>
public interface IReportStrings
{
    string ReportTitle { get; }
    string GeneratedAt { get; }
    string ScannedSize { get; }
    string Files { get; }
    string Folders { get; }
    string Reclaimable { get; }
    string SkippedErrors { get; }
    string ByCategory { get; }
    string AiAnalysis { get; }
    string MainCause { get; }
    string Findings { get; }
    string NextSteps { get; }

    /// <summary>Format string with one placeholder for the number of discarded suggestions.</summary>
    string AiRejectedNote { get; }

    string Recommendations { get; }
    string NoRecommendations { get; }
    string OfficialMethod { get; }
    string LargestItems { get; }
    string Name { get; }
    string Category { get; }
    string Size { get; }
    string SafetyNote { get; }
}

/// <summary>Plain English defaults, also used as the fallback when a label is missing.</summary>
public sealed class EnglishReportStrings : IReportStrings
{
    public static EnglishReportStrings Instance { get; } = new();

    public string ReportTitle => "Storage report";
    public string GeneratedAt => "Generated";
    public string ScannedSize => "Scanned";
    public string Files => "Files";
    public string Folders => "Folders";
    public string Reclaimable => "Reclaimable";
    public string SkippedErrors => "Skipped (errors)";
    public string ByCategory => "Usage by category";
    public string AiAnalysis => "AI analysis";
    public string MainCause => "Main cause";
    public string Findings => "Findings";
    public string NextSteps => "Next steps";
    public string AiRejectedNote => "{0} suggestion(s) from the model were discarded because they failed Storava's safety checks.";
    public string Recommendations => "Recommendations";
    public string NoRecommendations => "Nothing worth reclaiming was found in this scan.";
    public string OfficialMethod => "Recommended method";
    public string LargestItems => "Largest items";
    public string Name => "Name";
    public string Category => "Category";
    public string Size => "Size";
    public string SafetyNote => "Storava has not changed anything. Every action requires your explicit selection and confirmation.";
}
