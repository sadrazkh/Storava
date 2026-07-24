namespace Storava.Infrastructure.Persistence;

/// <summary>Generic key/value row used to persist serialized settings blobs.</summary>
public sealed class SettingEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
