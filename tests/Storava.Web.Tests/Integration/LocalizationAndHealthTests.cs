using System.Net;

namespace Storava.Web.Tests.Integration;

public sealed class LocalizationAndHealthTests(WebApplicationFactoryFixture factory)
    : IClassFixture<WebApplicationFactoryFixture>
{
    [Fact]
    public async Task Persian_request_renders_rtl_document()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/?culture=fa-IR");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        response.EnsureSuccessStatusCode();
        Assert.Contains("fa-IR", response.Content.Headers.ContentLanguage);
        Assert.Contains("""<html lang="fa-IR" dir="rtl" """, html, StringComparison.Ordinal);
        Assert.Contains("فضای ذخیره‌سازی را شفاف ببینید", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_endpoint_is_available()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
    }
}
