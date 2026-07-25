using Microsoft.Extensions.DependencyInjection;
using Storava.Application.Abstractions;
using Storava.Platform.Scanning;
using Storava.Platform.Security;
using Storava.Platform.Storage;

namespace Storava.Platform;

public static class DependencyInjection
{
    public static IServiceCollection AddStoravaPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IStorageInfoService, SystemStorageService>();
        services.AddSingleton<IProtectedPathService, ProtectedPathService>();
        services.AddSingleton<IDiskScanner, DiskScanner>();
        return services;
    }
}
