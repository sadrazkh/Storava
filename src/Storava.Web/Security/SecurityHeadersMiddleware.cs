namespace Storava.Web.Security;

public sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy =
            "default-src 'self'; " +
            "base-uri 'self'; " +
            "connect-src 'self' https://openrouter.ai; " +
            "font-src 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'; " +
            "img-src 'self' data:; " +
            "manifest-src 'self'; " +
            "object-src 'none'; " +
            "script-src 'self'; " +
            "style-src 'self'; " +
            "worker-src 'self' blob:;" +
            (environment.IsDevelopment() ? string.Empty : " upgrade-insecure-requests;");
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";

        await next(context);
    }
}
