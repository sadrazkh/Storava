using System.ComponentModel.DataAnnotations;

namespace Storava.Web.Models;

/// <summary>
/// What a companion Agent sends to attach itself to an account. It presents the code the user
/// typed in and the public half of a key pair it generated locally — the private half never
/// leaves the machine, and the server has no use for it.
/// </summary>
public sealed class AgentPairRequest
{
    [Required, StringLength(64)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Base64 SubjectPublicKeyInfo for a P-256 key.</summary>
    [Required, StringLength(512)]
    public string PublicKey { get; set; } = string.Empty;

    [StringLength(120)]
    public string DeviceName { get; set; } = string.Empty;
}

/// <summary>
/// What the Agent gets back. The channel secret is returned exactly once, at pairing, and is the
/// only copy the Agent will ever receive — the server keeps its own encrypted.
/// </summary>
public sealed record AgentPairResponse(
    Guid DeviceId,
    string DeviceName,
    string ChannelSecret,
    DateTimeOffset PairedAtUtc);

/// <summary>A refused pairing, with a reason the Agent can print in plain words.</summary>
public sealed record AgentPairProblem(string Reason, string Message);
