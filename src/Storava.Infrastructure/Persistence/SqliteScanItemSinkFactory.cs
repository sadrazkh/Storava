using Storava.Application.Abstractions;

namespace Storava.Infrastructure.Persistence;

public sealed class SqliteScanItemSinkFactory : IScanItemSinkFactory
{
    private readonly StoravaDbOptions _options;

    public SqliteScanItemSinkFactory(StoravaDbOptions options) => _options = options;

    public IScanItemSink Create(string sessionId) => new SqliteScanItemSink(sessionId, _options.ConnectionString);
}
