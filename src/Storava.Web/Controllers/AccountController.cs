using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Storava.Web.Data;
using Storava.Web.Models;
using Storava.Web.Services;

namespace Storava.Web.Controllers;

public sealed class AccountController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext database,
    IAccountSessionService sessionService,
    IAccountEmailSender emailSender,
    IOptions<AccountEmailOptions> emailOptions,
    IWebHostEnvironment environment,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider timeProvider) : Controller
{
    [AllowAnonymous]
    [HttpGet("/account/register")]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(new RegisterViewModel());
    }

    [AllowAnonymous]
    [EnableRateLimiting("account")]
    [HttpPost("/account/register")]
    public async Task<IActionResult> Register(
        RegisterViewModel model,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = model.DisplayName.Trim(),
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        var result = await userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(model);
        }

        var delivery = await SendConfirmationAsync(user, cancellationToken);
        return View("CheckEmail", new CheckEmailViewModel(
            delivery.Delivered,
            delivery.DevelopmentLink,
            IsPasswordReset: false));
    }

    [AllowAnonymous]
    [HttpGet("/account/login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(new LoginViewModel { ReturnUrl = SafeReturnUrl(returnUrl) });
    }

    [AllowAnonymous]
    [EnableRateLimiting("account")]
    [HttpPost("/account/login")]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        CancellationToken cancellationToken)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
            ModelState.AddModelError(string.Empty, localizer["AccountInvalidLogin"]);
            return View(model);
        }

        var passwordResult = await signInManager.CheckPasswordSignInAsync(
            user,
            model.Password,
            lockoutOnFailure: true);
        if (passwordResult.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, localizer["AccountLockedOut"]);
            return View(model);
        }

        if (passwordResult.IsNotAllowed)
        {
            ModelState.AddModelError(string.Empty, localizer["AccountEmailNotConfirmed"]);
            return View(model);
        }

        if (!passwordResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, localizer["AccountInvalidLogin"]);
            return View(model);
        }

        var accountSession = await sessionService.CreateAsync(
            user,
            Request.Headers.UserAgent.ToString(),
            model.RememberMe,
            cancellationToken);
        await signInManager.SignInWithClaimsAsync(
            user,
            model.RememberMe,
            [new Claim(AccountSessionService.SessionIdClaim, accountSession.Id.ToString("D"))]);

        user.LastLoginAtUtc = timeProvider.GetUtcNow();
        await userManager.UpdateAsync(user);

        return LocalRedirect(model.ReturnUrl ?? Url.Action(nameof(Index), "Account")!);
    }

    [AllowAnonymous]
    [HttpGet("/account/forgot-password")]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [AllowAnonymous]
    [EnableRateLimiting("account")]
    [HttpPost("/account/forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordViewModel model,
        CancellationToken cancellationToken)
    {
        AccountEmailDelivery delivery = new(true);
        if (ModelState.IsValid)
        {
            var user = await userManager.FindByEmailAsync(model.Email.Trim());
            if (user is not null && await userManager.IsEmailConfirmedAsync(user))
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                var link = BuildAccountLink(
                    nameof(ResetPassword),
                    new { userId = user.Id, code = token });
                delivery = await emailSender.SendPasswordResetAsync(
                    user,
                    link,
                    cancellationToken);
            }
        }

        return View("CheckEmail", new CheckEmailViewModel(
            delivery.Delivered,
            delivery.DevelopmentLink,
            IsPasswordReset: true));
    }

    [AllowAnonymous]
    [HttpGet("/account/resend-confirmation")]
    public IActionResult ResendConfirmation() => View(new ForgotPasswordViewModel());

    [AllowAnonymous]
    [EnableRateLimiting("account")]
    [HttpPost("/account/resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(
        ForgotPasswordViewModel model,
        CancellationToken cancellationToken)
    {
        AccountEmailDelivery delivery = new(true);
        if (ModelState.IsValid)
        {
            var user = await userManager.FindByEmailAsync(model.Email.Trim());
            if (user is not null && !await userManager.IsEmailConfirmedAsync(user))
            {
                delivery = await SendConfirmationAsync(user, cancellationToken);
            }
        }

        return View("CheckEmail", new CheckEmailViewModel(
            delivery.Delivered,
            delivery.DevelopmentLink,
            IsPasswordReset: false));
    }

    [AllowAnonymous]
    [HttpGet("/account/confirm-email")]
    public async Task<IActionResult> ConfirmEmail(Guid userId, string code)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        var succeeded = user is not null &&
            (await userManager.ConfirmEmailAsync(user, code)).Succeeded;
        return View(new ConfirmEmailViewModel(succeeded));
    }

    [AllowAnonymous]
    [HttpGet("/account/reset-password")]
    public IActionResult ResetPassword(Guid userId, string code)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(code))
        {
            return BadRequest();
        }

        return View(new ResetPasswordViewModel { UserId = userId, Code = code });
    }

    [AllowAnonymous]
    [EnableRateLimiting("account")]
    [HttpPost("/account/reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByIdAsync(model.UserId.ToString());
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, localizer["AccountResetInvalid"]);
            return View(model);
        }

        var result = await userManager.ResetPasswordAsync(user, model.Code, model.Password);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(model);
        }

        await userManager.UpdateSecurityStampAsync(user);
        ViewData["PasswordResetSucceeded"] = true;
        return View(model);
    }

    [Authorize]
    [HttpGet("/account")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var currentSessionId = sessionService.GetCurrentSessionId(User);
        var sessions = await sessionService.ListAsync(user.Id, cancellationToken);
        var deviceRecords = await database.UserDevices
            .AsNoTracking()
            .Where(device => device.UserId == user.Id && device.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        var devices = deviceRecords
            .OrderByDescending(device => device.LastSeenAtUtc)
            .Select(device => new AccountDeviceViewModel(
                device.Id,
                device.DisplayName,
                device.DeviceType,
                device.CreatedAtUtc,
                device.LastSeenAtUtc))
            .ToList();
        var usage = await database.UsageLedger
            .Where(entry => entry.UserId == user.Id)
            .SumAsync(entry => (long?)entry.Units, cancellationToken) ?? 0;

        return View(new AccountIndexViewModel(
            user.DisplayName,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            user.PlanCode,
            user.CreatedAtUtc,
            sessions.Select(session => new AccountSessionViewModel(
                session.Id,
                session.ClientLabel,
                session.CreatedAtUtc,
                session.LastSeenAtUtc,
                session.ExpiresAtUtc,
                session.Id == currentSessionId)).ToList(),
            devices,
            usage));
    }

    [Authorize]
    [EnableRateLimiting("account")]
    [HttpPost("/account/sessions/{sessionId:guid}/revoke")]
    public async Task<IActionResult> RevokeSession(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await sessionService.RevokeAsync(user.Id, sessionId, cancellationToken);
        if (sessionService.GetCurrentSessionId(User) == sessionId)
        {
            await signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize]
    [HttpPost("/account/logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sessionService.RevokeCurrentAsync(User, cancellationToken);
        await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    private async Task<AccountEmailDelivery> SendConfirmationAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = BuildAccountLink(
            nameof(ConfirmEmail),
            new { userId = user.Id, code = token });
        return await emailSender.SendConfirmationAsync(user, link, cancellationToken);
    }

    private string BuildAccountLink(string action, object routeValues)
    {
        var relative = Url.Action(action, "Account", routeValues)
            ?? throw new InvalidOperationException("Could not build account action URL.");
        var configuredBaseUrl = emailOptions.Value.PublicBaseUrl.Trim();
        if (Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var publicBase) &&
            publicBase.AbsolutePath == "/" &&
            string.IsNullOrEmpty(publicBase.Query) &&
            string.IsNullOrEmpty(publicBase.Fragment) &&
            string.IsNullOrEmpty(publicBase.UserInfo) &&
            (publicBase.Scheme == Uri.UriSchemeHttps ||
             ((environment.IsDevelopment() ||
               environment.IsEnvironment("Testing")) &&
              publicBase.Scheme == Uri.UriSchemeHttp)))
        {
            return new Uri(publicBase, relative).AbsoluteUri;
        }

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return Url.Action(action, "Account", routeValues, Request.Scheme)
                ?? throw new InvalidOperationException("Could not build local account action URL.");
        }

        throw new InvalidOperationException(
            "AccountEmail:PublicBaseUrl must be an absolute HTTPS URL in production.");
    }

    private string? SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : null;

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            var key = error.Code switch
            {
                "DuplicateEmail" or "DuplicateUserName" => "AccountDuplicateEmail",
                "PasswordTooShort" => "AccountPasswordTooShort",
                "PasswordRequiresDigit" => "AccountPasswordDigit",
                "PasswordRequiresLower" => "AccountPasswordLower",
                "PasswordRequiresUpper" => "AccountPasswordUpper",
                "PasswordRequiresNonAlphanumeric" => "AccountPasswordSymbol",
                _ => "AccountOperationFailed"
            };
            ModelState.AddModelError(string.Empty, localizer[key]);
        }
    }
}
