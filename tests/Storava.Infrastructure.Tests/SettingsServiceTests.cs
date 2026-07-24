using Microsoft.Extensions.DependencyInjection;
using Storava.Application.Abstractions;
using Storava.Application.Common;
using Storava.Infrastructure;

namespace Storava.Infrastructure.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"storava-test-{Guid.NewGuid():N}.db");

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStoravaInfrastructure(_dbPath);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Load_WithoutData_ReturnsDefaults()
    {
        using var provider = BuildProvider();
        var service = provider.GetRequiredService<ISettingsService>();

        await service.LoadAsync();

        Assert.Equal(AppLanguage.Persian, service.Current.Language);
        Assert.Equal(AppTheme.Dark, service.Current.Theme);
    }

    [Fact]
    public async Task Save_ThenReload_PersistsValues()
    {
        using (var provider = BuildProvider())
        {
            var service = provider.GetRequiredService<ISettingsService>();
            await service.LoadAsync();

            var updated = service.Current.Clone();
            updated.Language = AppLanguage.English;
            updated.Theme = AppTheme.Light;
            updated.AccentColor = "#123456";
            updated.Ai.Enabled = true;
            updated.Ai.ModelName = "deepseek/deepseek-r1:free";
            await service.SaveAsync(updated);
        }

        using (var provider = BuildProvider())
        {
            var service = provider.GetRequiredService<ISettingsService>();
            await service.LoadAsync();

            Assert.Equal(AppLanguage.English, service.Current.Language);
            Assert.Equal(AppTheme.Light, service.Current.Theme);
            Assert.Equal("#123456", service.Current.AccentColor);
            Assert.True(service.Current.Ai.Enabled);
            Assert.Equal("deepseek/deepseek-r1:free", service.Current.Ai.ModelName);
        }
    }

    [Fact]
    public async Task SaveAsync_RaisesSettingsChanged()
    {
        using var provider = BuildProvider();
        var service = provider.GetRequiredService<ISettingsService>();
        await service.LoadAsync();

        AppSettingsChangedProbe probe = new();
        service.SettingsChanged += probe.OnChanged;

        await service.SaveAsync(service.Current.Clone());

        Assert.True(probe.Raised);
    }

    private sealed class AppSettingsChangedProbe
    {
        public bool Raised { get; private set; }
        public void OnChanged(object? sender, Application.Settings.AppSettings e) => Raised = true;
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }
}
