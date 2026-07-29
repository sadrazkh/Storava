using Microsoft.Data.Sqlite;
using Storava.Application.Abstractions;

namespace Storava.Infrastructure.Persistence;

/// <summary>
/// The one way to get a SQLite connection, and the reason database work never runs on the caller's
/// thread.
/// <para>
/// This exists because of a mistake that was made twice. Microsoft.Data.Sqlite's asynchronous
/// methods are synchronous underneath — SQLite has no async file I/O, so <c>ExecuteReaderAsync</c>
/// and <c>ReadAsync</c> complete inline and awaiting them never yields. A page that awaits a query
/// therefore runs that query on the UI thread, and the interface stops. It was fixed once in the
/// query service alone; the repositories still did it, and measurement later showed stalls of up to
/// eighteen seconds while opening a page.
/// </para>
/// <para>
/// Fixing each method in turn would leave the next one free to reintroduce it. Routing every
/// connection through here means a method cannot touch the database without also leaving the
/// caller's thread, because the connection only exists inside the callback.
/// </para>
/// </summary>
public sealed class DatabaseGateway
{
    private readonly StoravaDbOptions _options;
    private readonly IDatabaseInitializer _initializer;

    public DatabaseGateway(StoravaDbOptions options, IDatabaseInitializer initializer)
    {
        _options = options;
        _initializer = initializer;
    }

    /// <summary>Runs a unit of database work on a pool thread and returns its result.</summary>
    public Task<T> RunAsync<T>(
        Func<SqliteConnection, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            // Ensuring the schema is part of every unit of work rather than something each caller
            // remembers. It returns immediately once it has run.
            await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            return await work(connection, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>Runs a unit of database work on a pool thread.</summary>
    public Task RunAsync(
        Func<SqliteConnection, CancellationToken, Task> work,
        CancellationToken cancellationToken = default) =>
        RunAsync<object?>(async (connection, token) =>
        {
            await work(connection, token).ConfigureAwait(false);
            return null;
        }, cancellationToken);
}
