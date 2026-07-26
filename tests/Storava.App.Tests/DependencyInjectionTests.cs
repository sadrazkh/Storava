using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storava.App;
using Storava.App.Services;
using Storava.App.ViewModels;

namespace Storava.App.Tests;

/// <summary>
/// Builds the application's real object graph against a throwaway folder and resolves every page.
/// A page wired into the navigation rail but never registered — or one whose constructor gained a
/// dependency nobody registered — fails only when the user clicks it; these tests fail at build
/// time instead.
/// </summary>
public class DependencyInjectionTests : IDisposable
{
    private readonly string _appData = Path.Combine(Path.GetTempPath(), $"storava-di-{Guid.NewGuid():N}");

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddStoravaApp(_appData);

        // Validated on build so a missing or circular dependency surfaces here rather than at the
        // first resolve, and scope mistakes (a singleton capturing a transient) are caught too.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    [Fact]
    public void EveryPageViewModelResolves()
    {
        using var provider = BuildProvider();

        Assert.All(AppComposition.PageViewModelTypes, type =>
        {
            var viewModel = provider.GetService(type);
            Assert.True(viewModel is not null, $"{type.Name} is not registered.");
        });
    }

    [Fact]
    public void NavigationServiceCanResolveEveryDestinationItAdvertises()
    {
        using var provider = BuildProvider();
        var navigation = provider.GetRequiredService<NavigationService>();

        // The rail offers these keys, so each has to lead to a page that can actually be built.
        foreach (var item in provider.GetRequiredService<ShellViewModel>().NavItems)
        {
            var exception = Record.Exception(() => navigation.NavigateTo(item.Key));
            Assert.True(exception is null, $"Navigating to '{item.Key}' threw: {exception?.Message}");
            Assert.Equal(item.Key, navigation.CurrentKey);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_appData))
                Directory.Delete(_appData, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
