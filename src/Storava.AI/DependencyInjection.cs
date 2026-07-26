using Microsoft.Extensions.DependencyInjection;
using Storava.AI.OpenRouter;
using Storava.AI.Validation;

namespace Storava.AI;

public static class DependencyInjection
{
    public static IServiceCollection AddStoravaAi(this IServiceCollection services)
    {
        services.AddHttpClient(OpenRouterProvider.HttpClientName);

        services.AddSingleton<AiPayloadBuilder>();
        services.AddSingleton<AiResponseValidator>();
        services.AddSingleton<IAiProvider, OpenRouterProvider>();
        services.AddSingleton<AiAdvisorService>();

        return services;
    }
}
