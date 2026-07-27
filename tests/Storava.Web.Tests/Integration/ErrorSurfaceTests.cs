using System.Net;

namespace Storava.Web.Tests.Integration;

/// <summary>
/// The status-code-pages middleware re-executes the error path with the <em>original</em> request
/// method. A form post that is rate limited or rejected therefore arrives at the error action as a
/// POST, and while that action answered only GET every such rejection came back as an empty 405
/// that hid the real status from the user and from the logs.
/// </summary>
public sealed class ErrorSurfaceTests(WebApplicationFactoryFixture factory)
    : IClassFixture<WebApplicationFactoryFixture>
{
    [Theory]
    [InlineData(429)]
    [InlineData(400)]
    [InlineData(404)]
    public async Task The_error_surface_reports_the_real_status_for_a_posted_request(int statusCode)
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            $"/Home/Error?statusCode={statusCode}",
            new FormUrlEncodedContent([]));

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_error_surface_still_answers_a_plain_get()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/Home/Error?statusCode=404");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsStringAsync());
    }
}
