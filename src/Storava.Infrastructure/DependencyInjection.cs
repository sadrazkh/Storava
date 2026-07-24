using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Storava.Application.Abstractions;
using Storava.Infrastructure.Persistence;
using Storava.Infrastructure.Settings;

namespace Storava.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence and settings. <paramref name="databasePath"/> is the full path
    /// to the local SQLite file (created on first use).
    /// </summary>
    public static IServiceCollection AddStoravaInfrastructure(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Shared
        }.ToString();

        services.AddDbContextFactory<StoravaDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<ISettingsService, SettingsService>();

        return services;
    }
}
