using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Application.Services;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Tests;

public class ScanSessionRepositoryTests
{
    [Fact]
    public async Task Save_ThenGet_RoundTripsAllFields()
    {
        using var host = new TestHost();
        var repository = host.Get<IScanSessionRepository>();

        var session = new ScanSession
        {
            Id = "session-1",
            RootPath = @"C:\Temp",
            Label = "my scan",
            Mode = ScanMode.Deep,
            Status = ScanStatus.Completed,
            StartedAt = new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 7, 24, 10, 5, 0, TimeSpan.Zero),
            TotalSize = 123456,
            TotalFiles = 42,
            TotalFolders = 7,
            ErrorCount = 2
        };

        await repository.SaveAsync(session);
        var loaded = await repository.GetAsync("session-1");

        Assert.NotNull(loaded);
        Assert.Equal(@"C:\Temp", loaded!.RootPath);
        Assert.Equal("my scan", loaded.Label);
        Assert.Equal(ScanMode.Deep, loaded.Mode);
        Assert.Equal(ScanStatus.Completed, loaded.Status);
        Assert.Equal(123456, loaded.TotalSize);
        Assert.Equal(42, loaded.TotalFiles);
        Assert.Equal(7, loaded.TotalFolders);
        Assert.Equal(2, loaded.ErrorCount);
        Assert.Equal(TimeSpan.FromMinutes(5), loaded.Duration);
    }

    [Fact]
    public async Task Save_Twice_UpdatesInsteadOfDuplicating()
    {
        using var host = new TestHost();
        var repository = host.Get<IScanSessionRepository>();

        var session = new ScanSession
        {
            Id = "session-2",
            RootPath = @"C:\A",
            Status = ScanStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        await repository.SaveAsync(session);

        session.Status = ScanStatus.Completed;
        session.TotalFiles = 10;
        await repository.SaveAsync(session);

        var recent = await repository.GetRecentAsync(10);
        Assert.Single(recent);
        Assert.Equal(ScanStatus.Completed, recent[0].Status);
        Assert.Equal(10, recent[0].TotalFiles);
    }

    [Fact]
    public async Task GetRecent_OrdersByStartedAtDescending()
    {
        using var host = new TestHost();
        var repository = host.Get<IScanSessionRepository>();
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < 3; i++)
        {
            await repository.SaveAsync(new ScanSession
            {
                Id = $"s{i}",
                RootPath = @"C:\",
                Status = ScanStatus.Completed,
                StartedAt = baseTime.AddHours(i)
            });
        }

        var recent = await repository.GetRecentAsync(10);
        Assert.Equal(["s2", "s1", "s0"], recent.Select(s => s.Id));
    }

    [Fact]
    public async Task Delete_RemovesSessionAndItems()
    {
        using var tree = new TestTree();
        tree.AddFile("a.bin", 100);

        using var host = new TestHost();
        var coordinator = host.Get<ScanCoordinator>();
        var result = await coordinator.RunAsync(
            new ScanRequest { RootPath = tree.Root },
            new Progress<ScanProgress>(),
            new PauseTokenSource().Token,
            CancellationToken.None);

        var repository = host.Get<IScanSessionRepository>();
        var query = host.Get<IScanQueryService>();

        Assert.NotEmpty(await query.GetRootsAsync(result.SessionId));

        await repository.DeleteAsync(result.SessionId);

        Assert.Null(await repository.GetAsync(result.SessionId));
        Assert.Empty(await query.GetRootsAsync(result.SessionId));
    }

    [Fact]
    public async Task Coordinator_RecordsSessionWithTotals()
    {
        using var tree = new TestTree();
        tree.AddFile("x.bin", 2048);

        using var host = new TestHost();
        var coordinator = host.Get<ScanCoordinator>();
        var result = await coordinator.RunAsync(
            new ScanRequest { RootPath = tree.Root, Label = "labelled" },
            new Progress<ScanProgress>(),
            new PauseTokenSource().Token,
            CancellationToken.None);

        var session = await host.Get<IScanSessionRepository>().GetAsync(result.SessionId);
        Assert.NotNull(session);
        Assert.Equal("labelled", session!.Label);
        Assert.Equal(ScanStatus.Completed, session.Status);
        Assert.Equal(2048, session.TotalSize);
        Assert.Equal(1, session.TotalFiles);
        Assert.NotNull(session.CompletedAt);
    }
}
