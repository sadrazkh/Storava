using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;
using Storava.Web.Data;

namespace Storava.Web.Services;

public sealed class AccountEmailOptions
{
    public string DeliveryMode { get; set; } = "Smtp";

    public string PublicBaseUrl { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Storava";
}

public sealed record AccountEmailDelivery(
    bool Delivered,
    string? DevelopmentLink = null);

public interface IAccountEmailSender
{
    Task<AccountEmailDelivery> SendConfirmationAsync(
        ApplicationUser user,
        string link,
        CancellationToken cancellationToken);

    Task<AccountEmailDelivery> SendPasswordResetAsync(
        ApplicationUser user,
        string link,
        CancellationToken cancellationToken);
}

public sealed class AccountEmailSender(
    IOptions<AccountEmailOptions> options,
    IWebHostEnvironment environment,
    ILogger<AccountEmailSender> logger) : IAccountEmailSender
{
    private readonly AccountEmailOptions _options = options.Value;

    public Task<AccountEmailDelivery> SendConfirmationAsync(
        ApplicationUser user,
        string link,
        CancellationToken cancellationToken) =>
        SendAsync(
            user,
            "Confirm your Storava account",
            "Confirm email",
            "Confirm this email address to activate your Storava account.",
            link,
            cancellationToken);

    public Task<AccountEmailDelivery> SendPasswordResetAsync(
        ApplicationUser user,
        string link,
        CancellationToken cancellationToken) =>
        SendAsync(
            user,
            "Reset your Storava password",
            "Reset password",
            "Use this link to choose a new Storava password. Ignore this message if you did not request it.",
            link,
            cancellationToken);

    private async Task<AccountEmailDelivery> SendAsync(
        ApplicationUser user,
        string subject,
        string action,
        string message,
        string link,
        CancellationToken cancellationToken)
    {
        if (IsDevelopmentDelivery())
        {
            return new AccountEmailDelivery(true, link);
        }

        if (!string.Equals(_options.DeliveryMode, "Smtp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(_options.Host) ||
            string.IsNullOrWhiteSpace(_options.FromAddress) ||
            string.IsNullOrWhiteSpace(user.Email))
        {
            logger.LogError(
                "Account email delivery is not configured. Set AccountEmail SMTP settings before enabling registration.");
            return new AccountEmailDelivery(false);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            IsBodyHtml = true,
            Body = BuildHtml(user.DisplayName, action, message, link)
        };
        mail.To.Add(new MailAddress(user.Email));

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);
        }

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
            return new AccountEmailDelivery(true);
        }
        catch (Exception exception) when (
            exception is SmtpException or InvalidOperationException or FormatException)
        {
            logger.LogError(exception, "Account email delivery failed.");
            return new AccountEmailDelivery(false);
        }
    }

    private bool IsDevelopmentDelivery() =>
        string.Equals(_options.DeliveryMode, "Development", StringComparison.OrdinalIgnoreCase) &&
        (environment.IsDevelopment() ||
         string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase));

    private static string BuildHtml(
        string displayName,
        string action,
        string message,
        string link)
    {
        var encoder = HtmlEncoder.Default;
        return $"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;padding:32px;background:#f3f4ec;color:#071a1c;font-family:system-ui,sans-serif">
              <main style="max-width:620px;margin:auto;border:1px solid #cad0c6;background:#fbfcf6;padding:32px">
                <p style="letter-spacing:.12em;text-transform:uppercase;color:#42605b">Storava account security</p>
                <h1 style="font-size:38px;line-height:1.05">{encoder.Encode(action)}</h1>
                <p>Hello {encoder.Encode(displayName)},</p>
                <p style="line-height:1.7">{encoder.Encode(message)}</p>
                <p><a href="{encoder.Encode(link)}" style="display:inline-block;padding:12px 20px;background:#0d3b39;color:#fff;text-decoration:none">{encoder.Encode(action)}</a></p>
                <p style="color:#61706d;font-size:13px">Storava never asks you to send a password or API key by email.</p>
              </main>
            </body>
            </html>
            """;
    }
}
