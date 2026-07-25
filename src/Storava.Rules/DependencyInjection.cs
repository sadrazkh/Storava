using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Storava.Application.Abstractions;
using Storava.Rules.Catalog;
using Storava.Rules.Scoring;

namespace Storava.Rules;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the rule engine, classification and local analysis. Also wires the classifying
    /// sink decorator so items are categorised as the scan streams them to storage.
    /// </summary>
    /// <typeparam name="TInnerSinkFactory">
    /// The persistence sink factory to decorate (registered by the infrastructure layer).
    /// </typeparam>
    public static IServiceCollection AddStoravaRules<TInnerSinkFactory>(this IServiceCollection services)
        where TInnerSinkFactory : class, IScanItemSinkFactory
    {
        services.AddSingleton<IRuleProvider, BuiltInRuleProvider>();
        services.AddSingleton<RuleEngine>();
        services.AddSingleton<ClassificationService>();
        services.AddSingleton<RecommendationScoreCalculator>();
        services.AddSingleton<RecommendationBuilder>();
        services.AddSingleton<AnalysisService>();

        // Replace (not append) so there is exactly one sink factory registration and it is
        // unambiguous that items get classified on their way to storage.
        services.Replace(ServiceDescriptor.Singleton<IScanItemSinkFactory>(sp =>
            new ClassifyingScanItemSinkFactory(
                sp.GetRequiredService<TInnerSinkFactory>(),
                sp.GetRequiredService<ClassificationService>())));

        return services;
    }
}
