using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Storava.AI;
using Storava.App.Models;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Domain.Common;
using Storava.Domain.Enums;
using Storava.Domain.ValueObjects;
using Storava.Reporting;
using Storava.Reporting.Export;
using Storava.Reporting.Model;

namespace Storava.App.ViewModels.Pages;

/// <summary>
/// Drives report generation, the AI advisory round and exports.
/// <para>
/// The AI flow is deliberately two-step: <c>Prepare</c> builds the sanitized payload and shows it
/// verbatim, and only an explicit approval unlocks <c>Send</c>. Changing anything invalidates the
/// approval, so data the user has not seen can never be transmitted.
/// </para>
/// </summary>
public sealed partial class ReportsViewModel : ViewModelBase, IDisposable
{
    private readonly ReportBuilder _reportBuilder;
    private readonly HtmlReportWriter _htmlWriter;
    private readonly JsonReportWriter _jsonWriter;
    private readonly CsvReportWriter _csvWriter;
    private readonly AiAdvisorService _advisor;
    private readonly IScanSessionRepository _sessions;

    /// <summary>Where the AI's findings are kept once it has produced them.</summary>
    private readonly IRecommendationRepository _recommendations;

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly ILocalizationService _localization;
    private readonly IFileSaver _fileSaver;
    private readonly ILogger<ReportsViewModel> _logger;

    private string? _sessionId;
    private StorageReport? _report;
    private AiPreview? _preview;
    private ReportAiSection? _aiSection;
    private CancellationTokenSource? _aiCancellation;

    [ObservableProperty] private bool _hasSession;
    [ObservableProperty] private string _rootPathText = string.Empty;
    [ObservableProperty] private string _scannedText = "—";
    [ObservableProperty] private string _reclaimableText = "—";
    [ObservableProperty] private int _recommendationCount;

    [NotifyPropertyChangedFor(nameof(AiAvailable))]
    [ObservableProperty] private bool _aiEnabled;

    [NotifyPropertyChangedFor(nameof(AiAvailable))]
    [ObservableProperty] private bool _aiKeyPresent;

    [ObservableProperty] private bool _isPreparing;

    [NotifyPropertyChangedFor(nameof(CanSend))]
    [ObservableProperty] private bool _isSending;

    [ObservableProperty] private string _aiModelName = string.Empty;

    [NotifyPropertyChangedFor(nameof(CanSend))]
    [ObservableProperty] private bool _hasPreview;

    [NotifyPropertyChangedFor(nameof(CanSend))]
    [ObservableProperty] private bool _payloadApproved;

    [NotifyPropertyChangedFor(nameof(CanSend))]
    [ObservableProperty] private bool _payloadBlocked;

    [ObservableProperty] private string _payloadJson = string.Empty;
    [ObservableProperty] private string? _aiSummary;
    [ObservableProperty] private string? _aiMainCause;
    [ObservableProperty] private string? _aiOverview;
    [ObservableProperty] private int _aiRejectedCount;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    public ReportsViewModel(
        ReportBuilder reportBuilder,
        HtmlReportWriter htmlWriter,
        JsonReportWriter jsonWriter,
        CsvReportWriter csvWriter,
        AiAdvisorService advisor,
        IScanSessionRepository sessions,
        IRecommendationRepository recommendations,
        ISettingsService settings,
        ISecretStore secrets,
        ILocalizationService localization,
        ScanController controller,
        IFileSaver fileSaver,
        ILogger<ReportsViewModel> logger)
    {
        _reportBuilder = reportBuilder;
        _htmlWriter = htmlWriter;
        _jsonWriter = jsonWriter;
        _csvWriter = csvWriter;
        _advisor = advisor;
        _sessions = sessions;
        _recommendations = recommendations;
        _settings = settings;
        _secrets = secrets;
        _localization = localization;
        _fileSaver = fileSaver;
        _logger = logger;

        _controller = controller;
        _localization.LanguageChanged += OnLanguageChanged;
        _settings.SettingsChanged += OnSettingsChanged;

        RefreshAiAvailability();
        _ = LoadAsync();
    }

    private readonly ScanController _controller;

    public ObservableCollection<string> AiFindings { get; } = [];
    public ObservableCollection<string> AiNextSteps { get; } = [];
    public ObservableCollection<RecommendationCardModel> AiSuggestions { get; } = [];

    public bool HasAiResult => AiSuggestions.Count > 0 || !string.IsNullOrWhiteSpace(AiSummary);

    /// <summary>The advisor is only offered once it is switched on and a key exists.</summary>
    public bool AiAvailable => AiEnabled && AiKeyPresent;

    /// <summary>Sending requires a prepared payload the user has explicitly approved.</summary>
    public bool CanSend => HasPreview && PayloadApproved && !PayloadBlocked && !IsSending;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // A regenerated report must be in the newly selected language.
        ResetPreview();
        _ = LoadAsync();
    }

    private void OnSettingsChanged(object? sender, Application.Settings.AppSettings e)
    {
        RefreshAiAvailability();
        // Settings changes alter what would be sent, so any approval is now stale.
        ResetPreview();
    }

    private void RefreshAiAvailability()
    {
        AiEnabled = _settings.Current.Ai.Enabled;
        AiKeyPresent = _secrets.Has(SecretNames.OpenRouterApiKey);
        AiModelName = _settings.Current.Ai.ModelName;
    }

    private async Task LoadAsync()
    {
        _sessionId = await ResolveSessionIdAsync().ConfigureAwait(true);
        HasSession = _sessionId is not null;
        if (_sessionId is null)
            return;

        await BuildReportAsync().ConfigureAwait(true);
    }

    private async Task<string?> ResolveSessionIdAsync()
    {
        if (!string.IsNullOrEmpty(_controller.CurrentSessionId))
            return _controller.CurrentSessionId;

        var recent = await _sessions.GetRecentAsync(1).ConfigureAwait(true);
        return recent.Count > 0 ? recent[0].Id : null;
    }

    private async Task BuildReportAsync()
    {
        if (_sessionId is null)
            return;

        var culture = _localization.CurrentCulture;
        _report = await _reportBuilder.BuildAsync(
            _sessionId,
            culture.TwoLetterISOLanguageName,
            CategoryLabel,
            RiskLabel,
            _aiSection).ConfigureAwait(true);

        RootPathText = _report.RootPath;
        ScannedText = new ByteSize(_report.TotalSize).Humanize(culture);
        ReclaimableText = new ByteSize(_report.TotalReclaimable).Humanize(culture);
        RecommendationCount = _report.Recommendations.Count;
    }

    private string CategoryLabel(StorageCategory category) => _localization[$"Str.Category.{category}"];

    private string RiskLabel(RiskLevel risk) => _localization[$"Str.Risk.{risk}"];

    // --- AI flow -----------------------------------------------------------------

    [RelayCommand]
    private async Task PrepareAsync()
    {
        if (_sessionId is null || IsPreparing)
            return;

        ResetPreview();
        IsPreparing = true;
        ErrorMessage = null;

        try
        {
            double targetFreeGb = _report is null
                ? 0
                : Math.Round((double)_report.TotalReclaimable / (1024 * 1024 * 1024), 1);

            _preview = await _advisor.PrepareAsync(
                _sessionId,
                _localization.CurrentCulture.TwoLetterISOLanguageName,
                targetFreeGb).ConfigureAwait(true);

            PayloadJson = _preview.RenderedJson;
            PayloadBlocked = !_preview.IsSafeToSend;
            HasPreview = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preparing the AI payload failed.");
            ErrorMessage = _localization["Str.Ai.Error"];
        }
        finally
        {
            IsPreparing = false;
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (_preview is null || !CanSend)
            return;

        IsSending = true;
        ErrorMessage = null;
        StatusMessage = null;
        _aiCancellation = new CancellationTokenSource();

        try
        {
            // The token is bound to the exact payload shown above.
            var approval = _preview.Approve();
            var result = await _advisor.AnalyzeAsync(
                approval,
                _localization.CurrentCulture.TwoLetterISOLanguageName,
                _aiCancellation.Token).ConfigureAwait(true);

            if (result.IsFailure)
            {
                ErrorMessage = DescribeError(result.Error);
                return;
            }

            ApplyAiResult(result.Value);

            // Kept, so the rest of the app can show it. Applying it only to this page is what left
            // the AI with nothing to say on the page where the user actually acts.
            if (_sessionId is { Length: > 0 } sessionId)
            {
                await _recommendations
                    .ReplaceAiAdviceAsync(sessionId, result.Value.Accepted)
                    .ConfigureAwait(true);
            }

            await BuildReportAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The AI analysis failed.");
            ErrorMessage = _localization["Str.Ai.Error"];
        }
        finally
        {
            IsSending = false;
            _aiCancellation?.Dispose();
            _aiCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelSend() => _aiCancellation?.Cancel();

    private void ApplyAiResult(AI.Validation.AiValidationResult result)
    {
        var culture = _localization.CurrentCulture;

        AiSummary = result.Summary;
        AiMainCause = result.MainCause;
        AiOverview = result.Overview;
        AiRejectedCount = result.Rejected.Count;

        AiFindings.Clear();
        foreach (var finding in result.Findings)
            AiFindings.Add(finding);

        AiNextSteps.Clear();
        foreach (var step in result.NextSteps)
            AiNextSteps.Add(step);

        AiSuggestions.Clear();
        foreach (var recommendation in result.Accepted)
            AiSuggestions.Add(new RecommendationCardModel(recommendation, culture, _localization));

        _aiSection = new ReportAiSection(
            _settings.Current.Ai.ModelName,
            DateTimeOffset.Now,
            result.Summary,
            result.MainCause,
            result.Overview,
            result.Findings,
            result.NextSteps,
            result.Accepted.Count,
            result.Rejected.Count);

        OnPropertyChanged(nameof(HasAiResult));
    }

    private string DescribeError(Error error)
    {
        string key = error.Code switch
        {
            "ai.no_key" => "Str.Ai.Error.NoKey",
            "ai.unauthorized" => "Str.Ai.Error.Unauthorized",
            "ai.rate_limited" => "Str.Ai.Error.RateLimited",
            "ai.timeout" => "Str.Ai.Error.Timeout",
            "ai.network" => "Str.Ai.Error.Network",
            "ai.malformed" or "ai.empty_response" => "Str.Ai.Error.Malformed",
            "ai.model_not_found" => "Str.Ai.Error.ModelNotFound",
            _ => "Str.Ai.Error"
        };

        return _localization[key];
    }

    private void ResetPreview()
    {
        _preview = null;
        PayloadJson = string.Empty;
        HasPreview = false;
        PayloadApproved = false;
        PayloadBlocked = false;
    }

    // --- Exports -----------------------------------------------------------------

    [RelayCommand]
    private async Task ExportHtmlAsync()
    {
        if (_report is null)
            return;

        var culture = _localization.CurrentCulture;
        string html = _htmlWriter.Write(_report, culture, new LocalizedReportStrings(_localization));
        await SaveAsync(html, "html", System.Text.Encoding.UTF8).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (_report is null)
            return;

        await SaveAsync(_jsonWriter.Write(_report), "json", System.Text.Encoding.UTF8).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (_report is null)
            return;

        string csv = _csvWriter.Write(_report, _localization.CurrentCulture);
        await SaveAsync(csv, "csv", CsvReportWriter.Encoding).ConfigureAwait(true);
    }

    private async Task SaveAsync(string content, string extension, System.Text.Encoding encoding)
    {
        string suggested = $"storava-report-{DateTime.Now:yyyyMMdd-HHmm}.{extension}";
        string? path = _fileSaver.Save(suggested, extension);
        if (path is null)
            return;

        await File.WriteAllTextAsync(path, content, encoding).ConfigureAwait(true);
        StatusMessage = _localization["Str.Reports.Exported"];
        _logger.LogInformation("Report exported as {Extension}.", extension);
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _settings.SettingsChanged -= OnSettingsChanged;
        _aiCancellation?.Cancel();
        _aiCancellation?.Dispose();
    }
}

/// <summary>Bridges the report writers to the app's resource dictionaries.</summary>
internal sealed class LocalizedReportStrings : IReportStrings
{
    private readonly ILocalizationService _localization;

    public LocalizedReportStrings(ILocalizationService localization) => _localization = localization;

    public string ReportTitle => _localization["Str.Reports.ScanReport"];
    public string GeneratedAt => _localization["Str.Reports.Summary"];
    public string ScannedSize => _localization["Str.Progress.Size"];
    public string Files => _localization["Str.Progress.Files"];
    public string Folders => _localization["Str.Progress.Folders"];
    public string Reclaimable => _localization["Str.Recommendations.Reclaimable"];
    public string SkippedErrors => _localization["Str.Progress.Errors"];
    public string ByCategory => _localization["Str.Analysis.Categories"];
    public string AiAnalysis => _localization["Str.Ai.Title"];
    public string MainCause => _localization["Str.Ai.MainCause"];
    public string Findings => _localization["Str.Ai.Findings"];
    public string NextSteps => _localization["Str.Ai.NextSteps"];
    public string AiRejectedNote => _localization["Str.Ai.RejectedNote"];
    public string Recommendations => _localization["Str.Recommendations.Title"];
    public string NoRecommendations => _localization["Str.Recommendations.NoneFound"];
    public string OfficialMethod => _localization["Str.Recommendations.OfficialMethod"];
    public string LargestItems => _localization["Str.Explorer.Largest"];
    public string Name => _localization["Str.Explorer.Col.Name"];
    public string Category => _localization["Str.Analysis.ColorBy.Category"];
    public string Size => _localization["Str.Explorer.Col.Size"];
    public string SafetyNote => _localization["Str.Recommendations.SafetyNote"];
}
