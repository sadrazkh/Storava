using Microsoft.Extensions.DependencyInjection;
using Storava.Application.Abstractions;
using Storava.Infrastructure;
using Storava.Infrastructure.Persistence;
using Storava.Platform;
using Storava.Rules;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// Wires the real infrastructure and platform services against a throwaway SQLite file, so
/// tests exercise the same code paths as the app.
/// </summary>
internal sealed class TestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    /// <param name="withRules">
    /// When true, the rules layer is added so items are classified as they are persisted —
    /// matching how the application composes these services.
    /// </param>
    /// <param name="databasePath">
    /// Reuse an earlier host's database, for tests that need a scan to survive a restart.
    /// </param>
    /// <param name="decorateSink">
    /// Wraps the sink the scanner writes through. It is the only deterministic place to interrupt
    /// a scan at an exact item, which is what the resume tests need — progress reports are
    /// throttled by wall-clock time and would make the point of interruption a race.
    /// </param>
    public TestHost(
        bool withRules = false,
        string? databasePath = null,
        Func<IScanItemSinkFactory, IScanItemSinkFactory>? decorateSink = null)
    {
        DatabasePath = databasePath ?? Path.Combine(Path.GetTempPath(), $"storava-test-{Guid.NewGuid():N}.db");
        _ownsDatabase = databasePath is null;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStoravaInfrastructure(DatabasePath);
        services.AddStoravaPlatform();
        if (withRules)
            services.AddStoravaRules<SqliteScanItemSinkFactory>();

        if (decorateSink is not null)
            DecorateSinkFactory(services, decorateSink);

        _provider = services.BuildServiceProvider();
    }

    private readonly bool _ownsDatabase;

    public string DatabasePath { get; }

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>
    /// Replaces the registered sink factory with one built on top of it, keeping whatever the
    /// rules layer may already have wrapped around the persistence sink.
    /// </summary>
    private static void DecorateSinkFactory(
        IServiceCollection services, Func<IScanItemSinkFactory, IScanItemSinkFactory> decorate)
    {
        var existing = services.Last(d => d.ServiceType == typeof(IScanItemSinkFactory));
        services.Remove(existing);

        services.AddSingleton<IScanItemSinkFactory>(provider =>
        {
            var inner = existing.ImplementationInstance as IScanItemSinkFactory
                ?? existing.ImplementationFactory?.Invoke(provider) as IScanItemSinkFactory
                ?? (IScanItemSinkFactory)ActivatorUtilities.CreateInstance(provider, existing.ImplementationType!);

            return decorate(inner);
        });
    }

    public void Dispose()
    {
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (!_ownsDatabase)
            return;

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                var path = DatabasePath + suffix;
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }
}
