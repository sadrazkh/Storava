using System.ComponentModel.DataAnnotations;

namespace Storava.Web.Models;

public sealed class RegisterViewModel
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(128, MinimumLength = 10)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class LoginViewModel
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(128)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public sealed class ForgotPasswordViewModel
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public string Code { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(128, MinimumLength = 10)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed record AccountSessionViewModel(
    Guid Id,
    string ClientLabel,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool IsCurrent);

public sealed record AccountDeviceViewModel(
    Guid Id,
    string DisplayName,
    string DeviceType,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc);

public sealed record AccountIndexViewModel(
    string DisplayName,
    string Email,
    bool EmailConfirmed,
    string PlanCode,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<AccountSessionViewModel> Sessions,
    IReadOnlyList<AccountDeviceViewModel> Devices,
    long RecordedUsageUnits);

public sealed record CheckEmailViewModel(
    bool DeliverySucceeded,
    string? DevelopmentLink,
    bool IsPasswordReset);

public sealed record ConfirmEmailViewModel(bool Succeeded);
