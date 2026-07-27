using Storava.Contracts.Agent;

namespace Storava.Web.Security;

public sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    /// <summary>
    /// The loopback addresses a companion Agent may listen on. Without these in <c>connect-src</c>
    /// the page could not reach an Agent at all — and listing exactly four fixed ports keeps that
    /// permission as narrow as the feature needs.
    /// <para>
    /// These are the only hosts besides the site itself and the AI provider the page may open a
    /// connection to, and every one of them is on this machine.
    /// </para>
    /// </summary>
    private static readonly string AgentConnectSources = string.Join(' ', AgentEndpoints.ConnectSources());

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy =
            "default-src 'self'; " +
            "base-uri 'self'; " +
            $"connect-src 'self' https://openrouter.ai https://eu.openrouter.ai {AgentConnectSources}; " +
            "font-src 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'; " +
            "img-src 'self' data:; " +
            "manifest-src 'self'; " +
            "object-src 'none'; " +
            "script-src 'self'; " +
            "style-src 'self'; " +
            "worker-src 'self' blob:;" +
            (environment.IsDevelopment() || !context.Request.IsHttps ? string.Empty : " upgrade-insecure-requests;");
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["Permissions-Policy"] =
            "accelerometer=(), autoplay=(), camera=(), display-capture=(), geolocation=(), " +
            "gyroscope=(), magnetometer=(), microphone=(), midi=(), payment=(), " +
            "publickey-credentials-get=(), screen-wake-lock=(), usb=()";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";

        await next(context);
    }
}
