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
        var contentSecurityPolicy = Header(response, "Content-Security-Policy");
        Assert.Contains("default-src 'self'", contentSecurityPolicy);
        Assert.Contains("connect-src 'self' https://openrouter.ai https://eu.openrouter.ai", contentSecurityPolicy);
        Assert.DoesNotContain("connect-src *", contentSecurityPolicy);
        Assert.DoesNotContain("upgrade-insecure-requests", contentSecurityPolicy);
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Contains("camera=()", Header(response, "Permissions-Policy"));
        Assert.Contains("display-capture=()", Header(response, "Permissions-Policy"));
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
        Assert.DoesNotContain(routeEndpoints, endpoint =>
            endpoint.RoutePattern.RawText?.Contains("proxy", StringComparison.OrdinalIgnoreCase) == true);

        var actionParameters = routeEndpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>())
            .Where(action => action is not null)
            .SelectMany(action => action!.Parameters)
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(IFormFile), actionParameters);
        Assert.DoesNotContain(typeof(IFormFileCollection), actionParameters);
    }

    [Fact]
    public async Task Production_client_bundles_do_not_publish_source_maps()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/dist/pages/scan.js.map");

        Assert.Equal(StatusCodes.Status404NotFound, (int)response.StatusCode);
    }

    [Fact]
    public async Task Browser_encoded_static_assets_keep_content_and_mime_type()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br, zstd");

        using var script = await client.GetAsync("/dist/pages/landing.js?browser=1");
        using var stylesheet = await client.GetAsync("/dist/assets/app.css?browser=1");
        var scriptBytes = await script.Content.ReadAsByteArrayAsync();
        var stylesheetBytes = await stylesheet.Content.ReadAsByteArrayAsync();

        script.EnsureSuccessStatusCode();
        stylesheet.EnsureSuccessStatusCode();
        Assert.Equal("text/javascript", script.Content.Headers.ContentType?.MediaType);
        Assert.Equal("text/css", stylesheet.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(scriptBytes);
        Assert.NotEmpty(stylesheetBytes);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        Assert.Single(response.Headers.GetValues(name));
}
