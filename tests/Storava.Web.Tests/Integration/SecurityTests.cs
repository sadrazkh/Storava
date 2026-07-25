using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Storava.Web.Tests.Integration;

public sealed class SecurityTests(WebApplicationFactoryFixture factory)
    : IClassFixture<WebApplicationFactoryFixture>
{
    [Fact]
    public async Task Landing_page_has_required_security_headers()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        Assert.Contains("default-src 'self'", Header(response, "Content-Security-Policy"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Contains("camera=()", Header(response, "Permissions-Policy"));
    }

    [Fact]
    public async Task Public_endpoint_surface_has_no_scan_file_upload()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();

        var endpointSources = factory.Services.GetServices<EndpointDataSource>();
        var endpoints = endpointSources.SelectMany(source => source.Endpoints).ToArray();
        var routeEndpoints = endpoints.OfType<RouteEndpoint>().ToArray();

        Assert.DoesNotContain(routeEndpoints, endpoint =>
            endpoint.RoutePattern.RawText?.Contains("upload", StringComparison.OrdinalIgnoreCase) == true);

        var actionParameters = routeEndpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>())
            .Where(action => action is not null)
            .SelectMany(action => action!.Parameters)
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(IFormFile), actionParameters);
        Assert.DoesNotContain(typeof(IFormFileCollection), actionParameters);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        Assert.Single(response.Headers.GetValues(name));
}
