namespace Storava.Application.Abstractions;

/// <summary>
/// Stores small secrets (currently the AI API key) encrypted at rest for the current user.
/// Implementations must never write the plaintext to logs, exports or reports.
/// </summary>
public interface ISecretStore
{
    /// <summary>Returns the stored secret, or null when none has been set.</summary>
    string? Get(string name);

    /// <summary>Stores the secret encrypted. Passing null or empty removes it.</summary>
    void Set(string name, string? value);

    bool Has(string name);
}

/// <summary>Known secret names.</summary>
public static class SecretNames
{
    public const string OpenRouterApiKey = "openrouter.api-key";
}
