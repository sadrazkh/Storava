using Storava.Application.Common;

namespace Storava.Application.Abstractions;

/// <summary>
/// Controls the active UI language and exposes localized string lookups. Changing the
/// language takes effect live, without an application restart.
/// </summary>
public interface ILocalizationService
{
    AppLanguage CurrentLanguage { get; }

    bool IsRightToLeft { get; }

    event EventHandler? LanguageChanged;

    void SetLanguage(AppLanguage language);

    /// <summary>Returns the localized string for a resource key, or the key itself if missing.</summary>
    string this[string key] { get; }
}
