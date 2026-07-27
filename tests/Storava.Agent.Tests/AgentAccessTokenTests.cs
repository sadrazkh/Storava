using System.Security.Cryptography;
using System.Text;
using Storava.Contracts.Agent;

namespace Storava.Agent.Tests;

/// <summary>
/// The access token is the only thing standing between a companion Agent and anything else on the
/// machine that can open a loopback socket. These tests are about what it refuses.
/// </summary>
public sealed class AgentAccessTokenTests
{
    private static readonly Guid Device = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string Origin = "https://storava.example";
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AgentTokenResult Verify(
        string token,
        string secret,
        Guid? device = null,
        string? origin = null,
        DateTimeOffset? now = null) =>
        AgentAccessToken.Verify(token, secret, device ?? Device, origin ?? Origin, now ?? Now);

    [Fact]
    public void A_token_this_agent_was_issued_is_accepted()
    {
        string secret = NewSecret();
        string token = AgentAccessToken.Issue(secret, Device, Origin, Now);

        var result = Verify(token, secret);

        Assert.Equal(AgentTokenStatus.Valid, result.Status);
        Assert.Equal(Device, result.DeviceId);
        Assert.Equal(Origin, result.Origin);
        Assert.Equal(Now.Add(AgentAccessToken.DefaultLifetime), result.ExpiresAt);
    }

    [Fact]
    public void A_token_signed_with_another_devices_secret_is_refused()
    {
        string token = AgentAccessToken.Issue(NewSecret(), Device, Origin, Now);

        // The point of a per-device secret: one leaked secret reaches exactly one machine.
        Assert.Equal(AgentTokenStatus.BadSignature, Verify(token, NewSecret()).Status);
    }

    [Fact]
    public void An_edited_payload_is_refused_even_though_it_reads_as_valid_json()
    {
        string secret = NewSecret();
        string token = AgentAccessToken.Issue(secret, Device, Origin, Now);
        var parts = token.Split('.');

        // Re-encode the payload with a far-future expiry, keeping the original signature.
        string forged = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $$"""{"deviceId":"{{Device}}","origin":"{{Origin}}","issuedAt":0,"expiresAt":99999999999}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var result = Verify($"{parts[0]}.{forged}.{parts[2]}", secret);

        Assert.Equal(AgentTokenStatus.BadSignature, result.Status);
    }

    [Fact]
    public void An_expired_token_is_refused_once_the_skew_allowance_is_past()
    {
        string secret = NewSecret();
        string token = AgentAccessToken.Issue(secret, Device, Origin, Now, TimeSpan.FromMinutes(5));

        // Still good inside the allowance, so two clocks a few seconds apart do not fight.
        Assert.True(Verify(token, secret, now: Now.AddMinutes(5).AddSeconds(30)).IsValid);
        Assert.Equal(
            AgentTokenStatus.Expired,
            Verify(token, secret, now: Now.AddMinutes(10)).Status);
    }

    [Fact]
    public void A_token_from_the_future_is_refused()
    {
        string secret = NewSecret();
        string token = AgentAccessToken.Issue(secret, Device, Origin, Now.AddHours(1));

        Assert.Equal(AgentTokenStatus.NotYetValid, Verify(token, secret).Status);
    }

    [Fact]
    public void A_token_for_another_device_is_refused()
    {
        string secret = NewSecret();
        string token = AgentAccessToken.Issue(secret, Guid.NewGuid(), Origin, Now);

        Assert.Equal(AgentTokenStatus.WrongDevice, Verify(token, secret).Status);
    }

    [Fact]
    public void A_token_minted_for_another_page_is_refused()
    {
        string secret = NewSecret();
        string token = AgentAccessToken.Issue(secret, Device, "https://not-storava.example", Now);

        // Binding to the origin means a token leaked from one deployment cannot drive an Agent
        // paired to another.
        Assert.Equal(AgentTokenStatus.WrongOrigin, Verify(token, secret).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-token")]
    [InlineData("storava1.only-two-parts")]
    [InlineData("storava2.abc.def")]
    [InlineData("storava1.!!!.???")]
    public void Anything_that_is_not_a_token_is_refused_without_throwing(string token)
    {
        var result = Verify(token, NewSecret());

        Assert.False(result.IsValid);
        Assert.Contains(result.Status, new[] { AgentTokenStatus.Malformed, AgentTokenStatus.BadSignature });
    }

    [Fact]
    public void A_null_token_is_refused()
    {
        Assert.Equal(
            AgentTokenStatus.Malformed,
            AgentAccessToken.Verify(null, NewSecret(), Device, Origin, Now).Status);
    }

    [Fact]
    public void An_absurdly_long_token_is_refused_before_it_is_parsed()
    {
        string token = "storava1." + new string('a', 5000) + ".b";

        Assert.Equal(AgentTokenStatus.Malformed, Verify(token, NewSecret()).Status);
    }

    [Fact]
    public void A_secret_the_agent_cannot_read_never_matches()
    {
        string token = AgentAccessToken.Issue(NewSecret(), Device, Origin, Now);

        Assert.Equal(AgentTokenStatus.BadSignature, Verify(token, "not-base64!").Status);
    }

    [Fact]
    public void Two_tokens_for_the_same_device_differ_only_by_their_window()
    {
        string secret = NewSecret();

        string first = AgentAccessToken.Issue(secret, Device, Origin, Now);
        string second = AgentAccessToken.Issue(secret, Device, Origin, Now.AddSeconds(1));

        Assert.NotEqual(first, second);
        Assert.True(Verify(first, secret).IsValid);
        Assert.True(Verify(second, secret, now: Now.AddSeconds(1)).IsValid);
    }

    [Theory]
    [InlineData("https://storava.example", "https://storava.example/")]
    [InlineData("https://Storava.Example", "https://storava.example")]
    public void Origins_are_compared_without_tripping_over_case_or_a_trailing_slash(string left, string right)
    {
        Assert.True(AgentAccessToken.OriginMatches(left, right));
    }

    [Theory]
    [InlineData("https://storava.example", "http://storava.example")]
    [InlineData("https://storava.example", "https://storava.example.evil")]
    [InlineData("https://storava.example", null)]
    public void Origins_that_differ_in_any_way_that_matters_do_not_match(string left, string? right)
    {
        Assert.False(AgentAccessToken.OriginMatches(left, right));
    }
}
