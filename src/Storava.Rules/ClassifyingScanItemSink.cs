using Storava.Application.Abstractions;
using Storava.Domain.Entities;

namespace Storava.Rules;

/// <summary>
/// Decorates the persistence sink so every item is classified on its way to storage. This
/// keeps the scanner focused on walking the file system and avoids a second pass over
/// millions of rows.
/// </summary>
public sealed class ClassifyingScanItemSink : IScanItemSink
{
    private readonly IScanItemSink _inner;
    private readonly ClassificationService _classification;

    public ClassifyingScanItemSink(IScanItemSink inner, ClassificationService classification)
    {
        _inner = inner;
        _classification = classification;
    }

    public ValueTask AddAsync(ScanItem item, CancellationToken cancellationToken = default)
    {
        _classification.Apply(item);
        return _inner.AddAsync(item, cancellationToken);
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
        => _inner.CompleteAsync(cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

/// <summary>Wraps an existing sink factory so all produced sinks classify as they write.</summary>
public sealed class ClassifyingScanItemSinkFactory : IScanItemSinkFactory
{
    private readonly IScanItemSinkFactory _inner;
    private readonly ClassificationService _classification;

    public ClassifyingScanItemSinkFactory(IScanItemSinkFactory inner, ClassificationService classification)
    {
        _inner = inner;
        _classification = classification;
    }

    public IScanItemSink Create(string sessionId)
        => new ClassifyingScanItemSink(_inner.Create(sessionId), _classification);
}
