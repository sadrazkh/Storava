using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Storava.Web.Tests.Integration;

public sealed class WebApplicationFactoryFixture : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"storava-web-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["Database:ApplyMigrations"] = "true",
                ["ConnectionStrings:AccountDatabase"] = $"Data Source={_databasePath}",
                ["AccountEmail:DeliveryMode"] = "Development",
                ["AccountEmail:PublicBaseUrl"] = "https://accounts.storava.test",
                // Every test in a class shares one loopback address, so the production per-IP
                // account limit would throttle the suite rather than the behaviour under test.
                // Rate limiting itself is covered by its own test, which sets its own limit.
                ["WebSecurity:AccountRateLimitPermit"] = "10000",
                ["WebSecurity:RateLimitPermit"] = "10000"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            DeleteDatabaseFile(_databasePath);
            DeleteDatabaseFile($"{_databasePath}-shm");
            DeleteDatabaseFile($"{_databasePath}-wal");
        }
    }

    private static void DeleteDatabaseFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
