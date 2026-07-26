namespace Storava.Web.Data;

public sealed class UsageLedgerEntry
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public string Meter { get; set; } = string.Empty;

    public long Units { get; set; }

    public string Source { get; set; } = string.Empty;

    public DateTimeOffset RecordedAtUtc { get; set; }
}
