using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Storava.Application.Abstractions;
using Storava.Platform.Security;

namespace Storava.Infrastructure.Tests;

public sealed class DpapiSecretStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "storava-secrets-" + Guid.NewGuid().ToString("N"));

    private DpapiSecretStore Create() => new(_directory, NullLogger<DpapiSecretStore>.Instance);

    [Fact]
    public void Get_ReturnsNullWhenNothingIsStored()
    {
        Assert.Null(Create().Get(SecretNames.OpenRouterApiKey));
        Assert.False(Create().Has(SecretNames.OpenRouterApiKey));
    }

    [Fact]
    public void Set_ThenGet_RoundTripsTheSecret()
    {
        var store = Create();
        store.Set(SecretNames.OpenRouterApiKey, "sk-or-v1-example-key");

        Assert.True(store.Has(SecretNames.OpenRouterApiKey));
        Assert.Equal("sk-or-v1-example-key", store.Get(SecretNames.OpenRouterApiKey));
    }

    [Fact]
    public void Set_SurvivesANewStoreInstance()
    {
        Create().Set(SecretNames.OpenRouterApiKey, "persisted-key");

        Assert.Equal("persisted-key", Create().Get(SecretNames.OpenRouterApiKey));
    }

    [Fact]
    public void StoredFile_DoesNotContainThePlaintext()
    {
        const string secret = "super-secret-api-key-value";
        Create().Set(SecretNames.OpenRouterApiKey, secret);

        foreach (var file in Directory.GetFiles(_directory))
        {
            byte[] bytes = File.ReadAllBytes(file);
            string asText = Encoding.UTF8.GetString(bytes);
            Assert.DoesNotContain(secret, asText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FileName_DoesNotRevealTheSecretName()
    {
        Create().Set(SecretNames.OpenRouterApiKey, "value");

        var names = Directory.GetFiles(_directory).Select(Path.GetFileName).ToList();
        Assert.All(names, n => Assert.DoesNotContain("openrouter", n!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Set_Null_RemovesTheSecret()
    {
        var store = Create();
        store.Set(SecretNames.OpenRouterApiKey, "value");
        store.Set(SecretNames.OpenRouterApiKey, null);

        Assert.False(store.Has(SecretNames.OpenRouterApiKey));
        Assert.Null(store.Get(SecretNames.OpenRouterApiKey));
    }

    [Fact]
    public void Set_Overwrites()
    {
        var store = Create();
        store.Set(SecretNames.OpenRouterApiKey, "first");
        store.Set(SecretNames.OpenRouterApiKey, "second");

        Assert.Equal("second", store.Get(SecretNames.OpenRouterApiKey));
    }

    [Fact]
    public void Get_ReturnsNullForACorruptedFileInsteadOfThrowing()
    {
        var store = Create();
        store.Set(SecretNames.OpenRouterApiKey, "value");

        foreach (var file in Directory.GetFiles(_directory))
            File.WriteAllBytes(file, [1, 2, 3, 4, 5]);

        Assert.Null(store.Get(SecretNames.OpenRouterApiKey));
    }

    [Fact]
    public void DifferentSecretsAreStoredSeparately()
    {
        var store = Create();
        store.Set("first.secret", "one");
        store.Set("second.secret", "two");

        Assert.Equal("one", store.Get("first.secret"));
        Assert.Equal("two", store.Get("second.secret"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
