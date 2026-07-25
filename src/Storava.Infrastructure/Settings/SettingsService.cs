using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Application.Settings;
using Storava.Infrastructure.Persistence;

namespace Storava.Infrastructure.Settings;

/// <summary>
/// Persists <see cref="AppSettings"/> as a single JSON blob in the Settings table.
/// Secrets (AI API key) are handled by a separate encrypted store and are never written here.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private const string SettingsKey = "app.settings";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDbContextFactory<StoravaDbContext> _contextFactory;
    private readonly IDatabaseInitializer _databaseInitializer;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsService(
        IDbContextFactory<StoravaDbContext> contextFactory,
        IDatabaseInitializer databaseInitializer,
        ILogger<SettingsService> logger)
    {
        _contextFactory = contextFactory;
        _databaseInitializer = databaseInitializer;
        _logger = logger;
    }

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _databaseInitializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var row = await db.Settings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == SettingsKey, cancellationToken)
                .ConfigureAwait(false);

            if (row is null)
            {
                Current = new AppSettings();
                _logger.LogInformation("No persisted settings found; using defaults.");
                return;
            }

            Current = JsonSerializer.Deserialize<AppSettings>(row.Value, JsonOptions) ?? new AppSettings();
            _logger.LogInformation("Settings loaded.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load settings; falling back to defaults.");
            Current = new AppSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var snapshot = settings.Clone();
        await _databaseInitializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == SettingsKey, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                db.Settings.Add(new SettingEntity { Key = SettingsKey, Value = json, UpdatedAt = DateTimeOffset.UtcNow });
            }
            else
            {
                row.Value = json;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            Current = snapshot;
            _logger.LogInformation("Settings saved.");
        }
        finally
        {
            _gate.Release();
        }

        SettingsChanged?.Invoke(this, Current);
    }
}
