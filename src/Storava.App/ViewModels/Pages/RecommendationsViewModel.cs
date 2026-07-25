using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Storava.App.Models;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Domain.ValueObjects;
using Storava.Rules;

namespace Storava.App.ViewModels.Pages;

public sealed partial class RecommendationsViewModel : ViewModelBase, IDisposable
{
    private readonly IRecommendationRepository _repository;
    private readonly IScanSessionRepository _sessions;
    private readonly AnalysisService _analysis;
    private readonly ScanController _controller;
    private readonly ILocalizationService _localization;
    private readonly ILogger<RecommendationsViewModel> _logger;

    private string? _sessionId;

    [ObservableProperty] private bool _hasSession;
    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private string _totalReclaimableText = "—";
    [ObservableProperty] private RecommendationCardModel? _selectedRecommendation;

    public RecommendationsViewModel(
        IRecommendationRepository repository,
        IScanSessionRepository sessions,
        AnalysisService analysis,
        ScanController controller,
        ILocalizationService localization,
        ILogger<RecommendationsViewModel> logger)
    {
        _repository = repository;
        _sessions = sessions;
        _analysis = analysis;
        _controller = controller;
        _localization = localization;
        _logger = logger;
        _localization.LanguageChanged += OnLanguageChanged;

        _ = LoadAsync();
    }

    public ObservableCollection<RecommendationCardModel> Recommendations { get; } = [];

    public bool HasRecommendations => Recommendations.Count > 0;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Recommendation text is generated in the active language, so regenerate on switch.
        _ = AnalyzeAsync();
    }

    private async Task LoadAsync()
    {
        _sessionId = await ResolveSessionIdAsync().ConfigureAwait(true);
        HasSession = _sessionId is not null;
        if (_sessionId is null)
            return;

        var stored = await _repository.GetBySessionAsync(_sessionId).ConfigureAwait(true);
        if (stored.Count == 0)
        {
            // First visit for this scan: analyse it now.
            await AnalyzeAsync().ConfigureAwait(true);
            return;
        }

        Populate(stored);
    }

    private async Task<string?> ResolveSessionIdAsync()
    {
        if (!string.IsNullOrEmpty(_controller.CurrentSessionId))
            return _controller.CurrentSessionId;

        var recent = await _sessions.GetRecentAsync(1).ConfigureAwait(true);
        return recent.Count > 0 ? recent[0].Id : null;
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        _sessionId ??= await ResolveSessionIdAsync().ConfigureAwait(true);
        HasSession = _sessionId is not null;
        if (_sessionId is null || IsAnalyzing)
            return;

        IsAnalyzing = true;
        try
        {
            string language = _localization.CurrentLanguage.ToCultureName().Split('-')[0];
            var results = await _analysis.AnalyzeAsync(_sessionId, language).ConfigureAwait(true);
            Populate(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local analysis failed for session {SessionId}.", _sessionId);
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private void Populate(IReadOnlyList<Domain.Entities.Recommendation> source)
    {
        var culture = _localization.CurrentCulture;

        Recommendations.Clear();
        foreach (var recommendation in source)
            Recommendations.Add(new RecommendationCardModel(recommendation, culture, _localization));

        long total = source.Sum(r => r.EstimatedSpace);
        TotalReclaimableText = new ByteSize(total).Humanize(culture);

        OnPropertyChanged(nameof(HasRecommendations));
        SelectedRecommendation = Recommendations.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenFolder(RecommendationCardModel? card)
    {
        if (card is null || !Directory.Exists(card.Path))
            return;

        // Read-only reveal in Explorer. Storava itself never modifies the folder.
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{card.Path}\"") { UseShellExecute = true });
    }

    public void Dispose() => _localization.LanguageChanged -= OnLanguageChanged;
}
