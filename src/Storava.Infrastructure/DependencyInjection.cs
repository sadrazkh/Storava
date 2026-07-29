using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Storava.Application.Abstractions;
using Storava.Application.History;
using Storava.Application.Planning;
using Storava.Application.Services;
using Storava.Contracts.Workspace;
using Storava.Infrastructure.Persistence;
using Storava.Infrastructure.Settings;
using Storava.Infrastructure.Workspace;

namespace Storava.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence, settings and scan storage. <paramref name="databasePath"/> is the
    /// full path to the local SQLite file (created on first use).
    /// </summary>
    /// <param name="identity">
    /// Which edition is doing the writing. The Agent shares this whole stack with the desktop
    /// application, so without being told it would stamp its archives as desktop-written and the
    /// manifest's one job — saying where a file came from — would be a lie.
    /// </param>
    public static IServiceCollection AddStoravaInfrastructure(
        this IServiceCollection services,
        string databasePath,
        ArchiveIdentity? identity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        services.AddSingleton(identity ?? ArchiveIdentity.Desktop);

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

        // Every repository takes its connections from here, which is what keeps database work
        // off whichever thread happened to ask for it.
        services.AddSingleton<DatabaseGateway>();
        services.AddSingleton<ISettingsService, SettingsService>();

        // Scan storage. The interface defaults to plain persistence; the rules layer replaces
        // it with a classifying decorator when that layer is added.
        services.AddSingleton<SqliteScanItemSinkFactory>();
        services.AddSingleton<IScanItemSinkFactory>(sp => sp.GetRequiredService<SqliteScanItemSinkFactory>());
        services.AddSingleton<IScanSessionRepository, ScanSessionRepository>();
        services.AddSingleton<IScanQueryService, ScanQueryService>();
        services.AddSingleton<IRecommendationRepository, RecommendationRepository>();
        services.AddSingleton<IStoragePlanRepository, StoragePlanRepository>();
        services.AddSingleton<IPlanExecutionRepository, PlanExecutionRepository>();

        // Scan orchestration (depends on IDiskScanner from the platform layer)
        services.AddSingleton<ScanCoordinator>();

        // Planning is advice-shaping only: it writes a document, it never touches the file system.
        services.AddSingleton<StoragePlanService>();

        // History reads back past scans and prunes its own tables; it cannot touch a user file either.
        services.AddSingleton<ScanHistoryService>();

        // Retention discards scans past the most recent few and gives the disk space back. Also
        // scan tables only: it removes measurements, never anything measured.
        services.AddSingleton<IDatabaseMaintenance, SqliteDatabaseMaintenance>();
        services.AddSingleton<ScanRetentionService>();

        // Portable .storava archives. Reads only the scan tables, so settings and the API key
        // are structurally excluded from every export.
        services.AddSingleton<IWorkspaceArchiveService, WorkspaceArchiveService>();

        return services;
    }
}
