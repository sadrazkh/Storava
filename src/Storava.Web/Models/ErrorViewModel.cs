namespace Storava.Web.Models;

public sealed class ErrorViewModel
{
    public string? RequestId { get; init; }

    public int StatusCode { get; init; } = StatusCodes.Status500InternalServerError;

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
