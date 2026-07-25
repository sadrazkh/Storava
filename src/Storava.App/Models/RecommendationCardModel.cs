using System.Globalization;
using System.Windows.Media;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Domain.ValueObjects;

namespace Storava.App.Models;

/// <summary>Display model for one recommendation card.</summary>
public sealed class RecommendationCardModel
{
    public RecommendationCardModel(
        Recommendation recommendation,
        CultureInfo culture,
        ILocalizationService localization)
    {
        Source = recommendation;

        Title = recommendation.Title;
        Reason = recommendation.Reason;
        Path = recommendation.Path;
        Technology = recommendation.Technology;
        Warning = recommendation.Warning;
        OfficialMigrationHint = recommendation.OfficialMigrationHint;

        SizeText = new ByteSize(recommendation.EstimatedSpace).Humanize(culture);
        CategoryText = localization[$"Str.Category.{recommendation.Category}"];
        RiskText = localization[$"Str.Risk.{recommendation.RiskLevel}"];
        RiskBrush = CategoryPalette.BrushForRisk(recommendation.RiskLevel);
        ConfidenceText = recommendation.Confidence.ToString("P0", culture);

        CanDelete = recommendation.CanDelete;
        CanMove = recommendation.CanMove;
        CanRegenerate = recommendation.CanRegenerate;
        HasOfficialMethod = recommendation.OfficialMigrationMethod == MigrationMethod.OfficialSetting;
        HasFallbackOnly = !HasOfficialMethod && recommendation.FallbackMigrationMethod != MigrationMethod.None;
        HasWarning = !string.IsNullOrWhiteSpace(recommendation.Warning);
    }

    /// <summary>The underlying advice. Read-only here; acting on it comes in a later phase.</summary>
    public Recommendation Source { get; }

    public string Title { get; }
    public string Reason { get; }
    public string Path { get; }
    public string? Technology { get; }
    public string? Warning { get; }
    public string? OfficialMigrationHint { get; }

    public string SizeText { get; }
    public string CategoryText { get; }
    public string RiskText { get; }
    public Brush RiskBrush { get; }
    public string ConfidenceText { get; }

    public bool CanDelete { get; }
    public bool CanMove { get; }
    public bool CanRegenerate { get; }
    public bool HasOfficialMethod { get; }
    public bool HasFallbackOnly { get; }
    public bool HasWarning { get; }
}
