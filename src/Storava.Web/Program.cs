using System.Globalization;
using System.IO.Compression;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Storava.Web.Data;
using Storava.Web.Security;
using Storava.Web.Services;

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
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.Configure<AccountEmailOptions>(
        builder.Configuration.GetSection("AccountEmail"));

    builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var provider = configuration["Database:Provider"] ?? "Postgres";
        var connectionString = configuration.GetConnectionString("AccountDatabase");
        if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                var localDataRoot = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                var databaseDirectory = Path.Combine(localDataRoot, "Storava", "Web");
                Directory.CreateDirectory(databaseDirectory);
                connectionString =
                    $"Data Source={Path.Combine(databaseDirectory, "storava-accounts.db")}";
            }

            options.UseSqlite(connectionString);
            return;
        }

        if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:AccountDatabase is required when Database:Provider is Postgres.");
            }

            options.UseNpgsql(connectionString, postgres =>
                postgres.EnableRetryOnFailure(3));
            return;
        }

        throw new InvalidOperationException(
            "Database:Provider must be either Postgres or Sqlite.");
    });

    builder.Services
        .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.SignIn.RequireConfirmedEmail = true;
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
            options.Password.RequiredUniqueChars = 4;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();
    // Cookie naming depends on the transport, not on taste: the __Host- prefix is only valid on a
    // Secure cookie, so it can only be used where the site is actually served over https.
    var productionCookies = !builder.Environment.IsDevelopment() &&
        !string.Equals(
            builder.Environment.EnvironmentName,
            "Testing",
            StringComparison.OrdinalIgnoreCase);

    builder.Services.ConfigureApplicationCookie(options =>
    {
        var productionCookie = productionCookies;
        options.Cookie.Name = productionCookie
            ? "__Host-Storava.Auth"
            : "Storava.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.Path = "/";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = productionCookie
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            await SecurityStampValidator.ValidatePrincipalAsync(context);
            if (context.Principal?.Identity?.IsAuthenticated != true)
            {
                return;
            }

            var accountSessions =
                context.HttpContext.RequestServices
                    .GetRequiredService<IAccountSessionService>();
            if (!await accountSessions.ValidateAsync(
                    context.Principal,
                    context.HttpContext.RequestAborted))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(
                    IdentityConstants.ApplicationScheme);
            }
        };
    });

    var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"];
    if (!string.IsNullOrWhiteSpace(dataProtectionPath))
    {
        Directory.CreateDirectory(dataProtectionPath);
        builder.Services
            .AddDataProtection()
            .SetApplicationName("Storava.Web")
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
    }

    builder.Services.AddScoped<IAccountSessionService, AccountSessionService>();
    builder.Services.AddScoped<IDevicePairingService, DevicePairingService>();
    builder.Services.AddScoped<IAccountEmailSender, AccountEmailSender>();
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
        // The __Host- prefix is only valid on a cookie that is also Secure, and a browser drops
        // one that is not. Over plain http — which is how development and the integration host
        // run — that meant the antiforgery cookie was never stored and every form post came back
        // as a rejected 400. The application cookie above switches names for the same reason.
        options.Cookie.Name = productionCookies
            ? "__Host-Storava.Antiforgery"
            : "Storava.Antiforgery";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = productionCookies
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
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
        // Sign-in, registration, reset and device pairing share one tighter bucket: they are the
        // endpoints worth guessing at. Configurable so a test host or an office behind one NAT
        // address is not throttled as though it were an attacker.
        var accountPermit = builder.Configuration.GetValue("WebSecurity:AccountRateLimitPermit", 10);
        options.AddPolicy("account", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = accountPermit,
                    Window = TimeSpan.FromMinutes(1),
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
        if (builder.Configuration.GetValue("WebSecurity:UseHttpsRedirection", true))
        {
            app.UseHttpsRedirection();
        }
    }

    app.UseResponseCompression();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseStaticFiles();
    app.UseRequestLocalization();
    app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");
    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapHealthChecks("/health").DisableRateLimiting();
    app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}")
        .WithStaticAssets();

    if (builder.Configuration.GetValue("Database:ApplyMigrations", true))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configuredDatabaseProvider =
            app.Configuration["Database:Provider"] ?? "Postgres";
        if (configuredDatabaseProvider.Equals(
                "Sqlite",
                StringComparison.OrdinalIgnoreCase))
        {
            await database.Database.EnsureCreatedAsync();
        }
        else
        {
            await database.Database.MigrateAsync();
        }
    }

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
