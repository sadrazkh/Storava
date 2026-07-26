using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Platform.Scanning;
using Storava.Platform.Security;
using Storava.Platform.Storage;

namespace Storava.Platform;

public static class DependencyInjection
{
    /// <param name="secretsDirectory">
    /// Where DPAPI-encrypted secrets are kept. Deliberately outside the scan database so they
    /// can never be picked up by an export.
    /// </param>
    public static IServiceCollection AddStoravaPlatform(this IServiceCollection services, string? secretsDirectory = null)
    {
        services.AddSingleton<IStorageInfoService, SystemStorageService>();
        services.AddSingleton<IProtectedPathService, ProtectedPathService>();
        services.AddSingleton<IDiskScanner, DiskScanner>();

        // The single implementation of the only interface that can change the file system.
        services.AddSingleton<IFileSystemActions, WindowsFileActions>();

        string directory = secretsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Storava", "secrets");

        services.AddSingleton<ISecretStore>(sp => new DpapiSecretStore(
            directory, sp.GetRequiredService<ILogger<DpapiSecretStore>>()));

        return services;
    }
}
