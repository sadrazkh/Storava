using System.Text.Json.Serialization;

namespace Storava.Contracts.Ai;

/// <summary>
/// The structured response Storava expects back. Anything outside this shape is rejected, and
/// every recommendation is re-validated against the local scan before it is ever shown.
/// </summary>
public sealed class AiResponse
{
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("mainCause")]
    public string? MainCause { get; set; }

    [JsonPropertyName("recommendations")]
    public List<AiRecommendation> Recommendations { get; set; } = [];

    [JsonPropertyName("report")]
    public AiReportSection? Report { get; set; }
}

public sealed class AiRecommendation
{
    /// <summary>Must match an id from the payload; anything else is discarded.</summary>
    [JsonPropertyName("scanItemId")]
    public string? ScanItemId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Expected to be "Move", "Delete", "Review" or "NoAction".</summary>
    [JsonPropertyName("actionSuggestion")]
    public string? ActionSuggestion { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>Expected to be "Low", "Medium" or "High".</summary>
    [JsonPropertyName("risk")]
    public string? Risk { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("estimatedSpaceGb")]
    public double EstimatedSpaceGb { get; set; }

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];
}

public sealed class AiReportSection
{
    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("findings")]
    public List<string> Findings { get; set; } = [];

    [JsonPropertyName("nextSteps")]
    public List<string> NextSteps { get; set; } = [];
}
