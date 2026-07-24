using System.Globalization;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Application.Common;

namespace Storava.App.Services;

/// <summary>
/// Swaps the active string dictionary at runtime and updates culture and layout
/// direction, so switching language takes effect immediately without a restart.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private static readonly IReadOnlyDictionary<AppLanguage, Uri> DictionaryUris = new Dictionary<AppLanguage, Uri>
    {
        [AppLanguage.Persian] = new("Resources/Localization/Strings.fa.xaml", UriKind.Relative),
        [AppLanguage.English] = new("Resources/Localization/Strings.en.xaml", UriKind.Relative)
    };

    private readonly ILogger<LocalizationService> _logger;

    public LocalizationService(ILogger<LocalizationService> logger) => _logger = logger;

    public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.Persian;

    public bool IsRightToLeft => CurrentLanguage.IsRightToLeft();

    public event EventHandler? LanguageChanged;

    public string this[string key]
    {
        get
        {
            var value = System.Windows.Application.Current?.TryFindResource(key);
            return value as string ?? key;
        }
    }

    public void SetLanguage(AppLanguage language)
    {
        var app = System.Windows.Application.Current
                  ?? throw new InvalidOperationException("Application is not initialized.");

        var merged = app.Resources.MergedDictionaries;

        // The active string dictionary is identified by carrying the App.FontFamily key.
        var existing = merged.FirstOrDefault(d => d.Contains("App.FontFamily"));
        var newDict = new ResourceDictionary { Source = DictionaryUris[language] };

        if (existing is not null)
        {
            int index = merged.IndexOf(existing);
            merged[index] = newDict;
        }
        else
        {
            merged.Add(newDict);
        }

        CurrentLanguage = language;

        var culture = CultureInfo.GetCultureInfo(language.ToCultureName());
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        _logger.LogInformation("Language switched to {Language} ({Culture}).", language, culture.Name);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
