using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Storava.Application.Abstractions;
using Storava.Application.Services;
using Storava.Infrastructure.Persistence;
using Storava.Infrastructure.Settings;

namespace Storava.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence, settings and scan storage. <paramref name="databasePath"/> is the
    /// full path to the local SQLite file (created on first use).
    /// </summary>
    public static IServiceCollection AddStoravaInfrastructure(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

        services.AddSingleton(new StoravaDbOptions
        {
            DatabasePath = databasePath,
            ConnectionString = connectionString
        });

        services.AddDbContextFactory<StoravaDbContext>(options => options.UseSqlite(connectionString));

        // Schema + settings
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<ISettingsService, SettingsService>();

        // Scan storage. The interface defaults to plain persistence; the rules layer replaces
        // it with a classifying decorator when that layer is added.
        services.AddSingleton<SqliteScanItemSinkFactory>();
        services.AddSingleton<IScanItemSinkFactory>(sp => sp.GetRequiredService<SqliteScanItemSinkFactory>());
        services.AddSingleton<IScanSessionRepository, ScanSessionRepository>();
        services.AddSingleton<IScanQueryService, ScanQueryService>();
        services.AddSingleton<IRecommendationRepository, RecommendationRepository>();

        // Scan orchestration (depends on IDiskScanner from the platform layer)
        services.AddSingleton<ScanCoordinator>();

        return services;
    }
}
