using System.Globalization;
using System.IO.Compression;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Storava.Web.Security;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, _, logger) => logger
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services
        .AddControllersWithViews(options =>
            options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
        .AddViewLocalization()
        .AddDataAnnotationsLocalization();

    builder.Services.AddLocalization();
    builder.Services.Configure<RequestLocalizationOptions>(options =>
    {
        var cultures = new[]
        {
            new CultureInfo("en-US"),
            new CultureInfo("fa-IR")
        };

        options.DefaultRequestCulture = new RequestCulture("en-US");
        options.SupportedCultures = cultures;
        options.SupportedUICultures = cultures;
        options.ApplyCurrentCultureToResponseHeaders = true;
    });

    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
    });
    builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
        options.Level = CompressionLevel.Fastest);
    builder.Services.Configure<GzipCompressionProviderOptions>(options =>
        options.Level = CompressionLevel.Fastest);

    builder.Services.AddAntiforgery(options =>
    {
        options.Cookie.Name = "__Host-Storava.Antiforgery";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.HeaderName = "X-Storava-Antiforgery";
    });

    var rateLimitPermit = builder.Configuration.GetValue("WebSecurity:RateLimitPermit", 120);
    var rateLimitWindow = builder.Configuration.GetValue("WebSecurity:RateLimitWindowSeconds", 60);
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitPermit,
                    Window = TimeSpan.FromSeconds(rateLimitWindow),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
    });

    builder.Services.AddHealthChecks();
    builder.Services.AddProblemDetails();

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    app.UseResponseCompression();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseRequestLocalization();
    app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");
    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthorization();
    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapHealthChecks("/health").DisableRateLimiting();
    app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}")
        .WithStaticAssets();

    app.Run();
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "Storava Web terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
