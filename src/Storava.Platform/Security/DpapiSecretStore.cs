using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;

namespace Storava.Platform.Security;

/// <summary>
/// Encrypts secrets with Windows DPAPI scoped to the current user, so the file is unreadable by
/// other accounts and useless if copied to another machine. Secrets live outside the scan
/// database, which keeps them out of every export.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    // Ties the ciphertext to this application, so another app's DPAPI blob cannot be swapped in.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Storava.SecretStore.v1");

    private readonly string _directory;
    private readonly ILogger<DpapiSecretStore> _logger;

    public DpapiSecretStore(string directory, ILogger<DpapiSecretStore> logger)
    {
        _directory = directory;
        _logger = logger;
        Directory.CreateDirectory(_directory);
    }

    public string? Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string path = PathFor(name);
        if (!File.Exists(path))
            return null;

        try
        {
            byte[] encrypted = File.ReadAllBytes(path);
            byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException ex)
        {
            // Wrong user or a corrupted file: report the failure without echoing anything.
            _logger.LogWarning(ex, "A stored secret could not be decrypted and will be ignored.");
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "A stored secret could not be read.");
            return null;
        }
    }

    public void Set(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string path = PathFor(name);

        if (string.IsNullOrEmpty(value))
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "A stored secret could not be removed.");
            }

            return;
        }

        byte[] plain = Encoding.UTF8.GetBytes(value);
        byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        Array.Clear(plain);

        File.WriteAllBytes(path, encrypted);
        _logger.LogInformation("Secret '{Name}' was updated.", name);
    }

    public bool Has(string name) => File.Exists(PathFor(name));

    private string PathFor(string name)
    {
        // Keep the file name opaque and filesystem-safe.
        string safe = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..16];
        return Path.Combine(_directory, $"{safe}.bin");
    }
}
