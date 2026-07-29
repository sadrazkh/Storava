using Storava.Application.Common;

namespace Storava.Application.Settings;

/// <summary>
/// User-facing application settings persisted locally. The AI section never contains
/// the raw API key here: the key is stored separately and encrypted (DPAPI).
/// </summary>
public sealed class AppSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.Persian;
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>Accent color as a hex string (#RRGGBB).</summary>
    public string AccentColor { get; set; } = "#0FB5AE";

    /// <summary>Set once the onboarding flow has been completed.</summary>
    public bool OnboardingCompleted { get; set; }

    /// <summary>
    /// How many scans to keep. Older ones are discarded automatically once a new scan finishes,
    /// because a full-drive scan is millions of rows and nothing else ever removed them.
    /// </summary>
    public int KeepRecentScans { get; set; } = 3;

    public AiSettings Ai { get; set; } = new();

    public AppSettings Clone() => new()
    {
        Language = Language,
        Theme = Theme,
        AccentColor = AccentColor,
        OnboardingCompleted = OnboardingCompleted,
        KeepRecentScans = KeepRecentScans,
        Ai = Ai.Clone()
    };
}

/// <summary>AI/OpenRouter configuration. The API key itself is stored out of band.</summary>
public sealed class AiSettings
{
    public bool Enabled { get; set; }
    public string ModelName { get; set; } = "openrouter/free";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public double Temperature { get; set; } = 0.2;
    public int MaxTokens { get; set; } = 2048;
    public int TimeoutSeconds { get; set; } = 60;
    public int RetryCount { get; set; } = 2;
    // There is deliberately no "send real paths" switch: sanitisation is unconditional, and a
    // setting that could turn it off would contradict the guarantee shown on the consent screen.
    public bool AllowUnknownFolderAnalysis { get; set; } = true;
    public bool AllowReportGeneration { get; set; } = true;

    public AiSettings Clone() => (AiSettings)MemberwiseClone();
}
