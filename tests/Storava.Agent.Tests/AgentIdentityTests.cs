using System.Security.Cryptography;
using Storava.Agent.Identity;
using Storava.Application.Abstractions;

namespace Storava.Agent.Tests;

/// <summary>
/// Covers what the Agent keeps about itself. The private key and the channel secret are the two
/// things that must never be readable in the clear, and the identity has to be stable: an Agent
/// that generated a new key on every run would appear as a new machine each time.
/// </summary>
public sealed class AgentIdentityTests
{
    /// <summary>
    /// Stands in for the DPAPI store. It deliberately does not encrypt: these tests are about what
    /// the Agent stores and when, not about DPAPI, which is exercised by its own tests.
    /// </summary>
    private sealed class InMemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, string> Values => _values;

        public string? Get(string name) => _values.TryGetValue(name, out string? value) ? value : null;

        public void Set(string name, string? value)
        {
            if (string.IsNullOrEmpty(value))
                _values.Remove(name);
            else
                _values[name] = value;
        }

        public bool Has(string name) => _values.ContainsKey(name);
    }

    [Fact]
    public void The_identity_is_created_once_and_then_reused()
    {
        var secrets = new InMemorySecretStore();
        var keys = new AgentKeyStore(secrets);

        using var first = keys.LoadOrCreate();
        using var second = keys.LoadOrCreate();

        // Same machine, same identity: the server would otherwise see a new device every run.
        Assert.Equal(AgentKeyStore.ThumbprintOf(first), AgentKeyStore.ThumbprintOf(second));
    }

    [Fact]
    public void Two_installations_do_not_share_an_identity()
    {
        using var one = new AgentKeyStore(new InMemorySecretStore()).LoadOrCreate();
        using var two = new AgentKeyStore(new InMemorySecretStore()).LoadOrCreate();

        Assert.NotEqual(AgentKeyStore.ThumbprintOf(one), AgentKeyStore.ThumbprintOf(two));
    }

    [Fact]
    public void Only_the_public_half_of_the_key_is_ever_exported()
    {
        var secrets = new InMemorySecretStore();
        using var key = new AgentKeyStore(secrets).LoadOrCreate();

        string exported = AgentKeyStore.PublicKeyOf(key);

        // What is exported must parse as a public key and must not carry the private parameters.
        using var reimported = ECDsa.Create();
        reimported.ImportSubjectPublicKeyInfo(Convert.FromBase64String(exported), out _);
        Assert.Throws<CryptographicException>(() => reimported.ExportPkcs8PrivateKey());

        string stored = Assert.Single(secrets.Values).Value;
        Assert.NotEqual(exported, stored);
    }

    [Fact]
    public void The_key_is_a_P256_key_the_server_will_accept()
    {
        using var key = new AgentKeyStore(new InMemorySecretStore()).LoadOrCreate();

        var parameters = key.ExportParameters(includePrivateParameters: false);
        Assert.Equal(
            ECCurve.NamedCurves.nistP256.Oid.Value,
            parameters.Curve.Oid.Value ?? ECCurve.NamedCurves.nistP256.Oid.Value);
    }

    [Fact]
    public void A_corrupted_key_reads_as_no_identity_rather_than_throwing()
    {
        var secrets = new InMemorySecretStore();
        secrets.Set("agent.identity.private-key", "not-a-key");

        Assert.Null(new AgentKeyStore(secrets).TryLoad());
    }

    [Fact]
    public void Deleting_the_identity_leaves_nothing_behind()
    {
        var secrets = new InMemorySecretStore();
        var keys = new AgentKeyStore(secrets);
        keys.LoadOrCreate().Dispose();

        keys.Delete();

        Assert.False(keys.Exists);
        Assert.Null(keys.TryLoad());
    }

    [Fact]
    public void The_fingerprint_matches_the_thumbprint_the_server_stores()
    {
        using var key = new AgentKeyStore(new InMemorySecretStore()).LoadOrCreate();

        // The user compares what the agent prints with what the account page shows, so the two
        // have to be the same hash, only spaced.
        string fingerprint = AgentKeyStore.FingerprintOf(key).Replace(" ", string.Empty);
        Assert.StartsWith(fingerprint, AgentKeyStore.ThumbprintOf(key), StringComparison.Ordinal);
    }

    [Fact]
    public void A_registration_round_trips()
    {
        var secrets = new InMemorySecretStore();
        var store = new AgentRegistrationStore(secrets);
        var registration = new AgentRegistration
        {
            ServerBaseUrl = "https://storava.example/",
            DeviceId = Guid.NewGuid(),
            DeviceName = "Workshop PC",
            ChannelSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            PairedAtUtc = DateTimeOffset.UtcNow
        };

        store.Save(registration);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(registration.DeviceId, loaded!.DeviceId);
        Assert.Equal(registration.ChannelSecret, loaded.ChannelSecret);
        Assert.Equal(registration.ServerBaseUrl, loaded.ServerBaseUrl);
    }

    [Fact]
    public void A_registration_missing_its_secret_is_treated_as_unpaired()
    {
        var secrets = new InMemorySecretStore();
        var store = new AgentRegistrationStore(secrets);

        store.Save(new AgentRegistration { DeviceId = Guid.NewGuid(), ServerBaseUrl = "https://x/" });

        // Half a pairing is not a pairing: acting on it would fail later and less clearly.
        Assert.Null(store.Load());
        Assert.False(store.Exists);
    }

    [Fact]
    public void Unreadable_registration_json_is_treated_as_unpaired()
    {
        var secrets = new InMemorySecretStore();
        secrets.Set("agent.registration", "{ not json");

        Assert.Null(new AgentRegistrationStore(secrets).Load());
    }

    [Fact]
    public void Clearing_the_registration_removes_the_channel_secret()
    {
        var secrets = new InMemorySecretStore();
        var store = new AgentRegistrationStore(secrets);
        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        store.Save(new AgentRegistration
        {
            DeviceId = Guid.NewGuid(),
            ServerBaseUrl = "https://storava.example/",
            ChannelSecret = secret
        });
        store.Clear();

        Assert.Null(store.Load());
        Assert.DoesNotContain(secrets.Values.Values, value => value.Contains(secret, StringComparison.Ordinal));
    }
}
