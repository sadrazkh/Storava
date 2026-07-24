using Storava.Application.Settings;

namespace Storava.Application.Abstractions;

/// <summary>
/// Reads and persists <see cref="AppSettings"/>. Implementations must never write the
/// AI API key in plain text and must not include secrets in exports or logs.
/// </summary>
public interface ISettingsService
{
    /// <summary>The current, in-memory settings snapshot.</summary>
    AppSettings Current { get; }

    /// <summary>Raised after settings are saved, with the new snapshot.</summary>
    event EventHandler<AppSettings>? SettingsChanged;

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
