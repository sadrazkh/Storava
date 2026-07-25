using System.Globalization;
using Storava.Application.Common;

namespace Storava.Application.Abstractions;

/// <summary>
/// Controls the active UI language and exposes localized string lookups. Changing the
/// language takes effect live, without an application restart.
/// </summary>
public interface ILocalizationService
{
    AppLanguage CurrentLanguage { get; }

    /// <summary>
    /// The culture matching <see cref="CurrentLanguage"/>. Formatting code should use this
    /// explicitly rather than the ambient thread culture, which can differ across async hops.
    /// </summary>
    CultureInfo CurrentCulture { get; }

    bool IsRightToLeft { get; }

    event EventHandler? LanguageChanged;

    void SetLanguage(AppLanguage language);

    /// <summary>Returns the localized string for a resource key, or the key itself if missing.</summary>
    string this[string key] { get; }
}
