using Microsoft.Extensions.DependencyInjection;
using Storava.Reporting.Export;

namespace Storava.Reporting;

public static class DependencyInjection
{
    public static IServiceCollection AddStoravaReporting(this IServiceCollection services)
    {
        services.AddSingleton<ReportBuilder>();
        services.AddSingleton<JsonReportWriter>();
        services.AddSingleton<HtmlReportWriter>();
        services.AddSingleton<CsvReportWriter>();
        return services;
    }
}
