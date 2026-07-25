using Microsoft.Extensions.DependencyInjection;
using Storava.Infrastructure;
using Storava.Platform;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// Wires the real infrastructure and platform services against a throwaway SQLite file, so
/// tests exercise the same code paths as the app.
/// </summary>
internal sealed class TestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public TestHost()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"storava-test-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStoravaInfrastructure(DatabasePath);
        services.AddStoravaPlatform();
        _provider = services.BuildServiceProvider();
    }

    public string DatabasePath { get; }

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public void Dispose()
    {
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

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
