using System.Security.Cryptography;
using Storava.Application.Abstractions;

namespace Storava.Agent.Identity;

/// <summary>
/// The Agent's own key pair, which is what makes one installation distinguishable from another.
/// <para>
/// The private half is generated on this machine, encrypted at rest with the same DPAPI store the
/// desktop app uses for the AI key, and never sent anywhere. Only the public half is presented at
/// pairing. Copying the key file to another machine or another Windows account yields nothing:
/// DPAPI will not decrypt it.
/// </para>
/// </summary>
public sealed class AgentKeyStore(ISecretStore secrets)
{
    internal const string PrivateKeySecret = "agent.identity.private-key";

    /// <summary>Loads the existing key, or creates one the first time the Agent runs.</summary>
    public ECDsa LoadOrCreate()
    {
        var existing = TryLoad();
        if (existing is not null)
            return existing;

        var created = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        secrets.Set(PrivateKeySecret, Convert.ToBase64String(created.ExportPkcs8PrivateKey()));
        return created;
    }

    /// <summary>Returns the stored key, or null when there is none this account can read.</summary>
    public ECDsa? TryLoad()
    {
        string? stored = secrets.Get(PrivateKeySecret);
        if (string.IsNullOrWhiteSpace(stored))
            return null;

        var key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(stored), out _);
            return key;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            key.Dispose();
            return null;
        }
    }

    public bool Exists => secrets.Has(PrivateKeySecret);

    /// <summary>Base64 SubjectPublicKeyInfo — the only part of the key that ever leaves.</summary>
    public static string PublicKeyOf(ECDsa key) =>
        Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    /// <summary>
    /// The same hash of the public key the server stores, so the user can compare what the Agent
    /// prints with what their account page shows and confirm they are looking at one machine.
    /// </summary>
    public static string ThumbprintOf(ECDsa key) =>
        Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    /// <summary>The short, spaced form shown to people.</summary>
    public static string FingerprintOf(ECDsa key)
    {
        string thumbprint = ThumbprintOf(key);
        return string.Join(' ', Enumerable.Range(0, 4).Select(index => thumbprint.Substring(index * 4, 4)));
    }

    /// <summary>
    /// Forgets this installation's identity. Used by <c>unpair</c>, so a machine that is removed
    /// from an account cannot silently re-present the key the server still has on file.
    /// </summary>
    public void Delete() => secrets.Set(PrivateKeySecret, null);
}
