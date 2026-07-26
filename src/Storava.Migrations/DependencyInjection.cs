using Microsoft.Extensions.DependencyInjection;

namespace Storava.Migrations;

public static class DependencyInjection
{
    /// <summary>
    /// Registers plan execution. <c>IFileSystemActions</c> is not registered here — it belongs to
    /// the platform layer, so an application that never adds the platform layer simply has no way
    /// to run a step.
    /// </summary>
    public static IServiceCollection AddStoravaMigrations(this IServiceCollection services)
    {
        services.AddSingleton<ExecutionGuard>();
        services.AddSingleton<PlanExecutionService>();
        return services;
    }
}
