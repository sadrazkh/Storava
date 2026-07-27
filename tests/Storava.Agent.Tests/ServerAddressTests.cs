namespace Storava.Agent.Tests;

/// <summary>
/// The address given to <c>pair</c> is where the Agent sends a code and receives a secret it will
/// trust from then on. A typo must not be able to put either on the wire in the clear.
/// </summary>
public sealed class ServerAddressTests
{
    [Theory]
    [InlineData("https://storava.example", "https://storava.example/")]
    [InlineData("https://storava.example/", "https://storava.example/")]
    [InlineData("https://storava.example/app", "https://storava.example/app/")]
    public void An_https_address_is_accepted_and_given_a_base_slash(string input, string expected)
    {
        Assert.True(ServerAddress.TryParse(input, out var address, out _));

        // Without the trailing slash, resolving "api/agent/pair" against it would eat the last
        // segment and quietly post somewhere else.
        Assert.Equal(expected, address.ToString());
    }

    [Theory]
    [InlineData("http://localhost:5120")]
    [InlineData("http://127.0.0.1:5120")]
    public void Plain_http_is_allowed_only_to_a_loopback_address(string input)
    {
        Assert.True(ServerAddress.TryParse(input, out _, out _));
    }

    [Fact]
    public void Plain_http_to_another_host_is_refused()
    {
        Assert.False(ServerAddress.TryParse("http://storava.example", out _, out string error));
        Assert.Contains("HTTP", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("storava.example")]
    [InlineData("ftp://storava.example")]
    [InlineData("file:///C:/windows")]
    public void Anything_that_is_not_an_absolute_web_address_is_refused(string? input)
    {
        Assert.False(ServerAddress.TryParse(input, out _, out string error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void The_pairing_endpoint_resolves_under_the_given_base()
    {
        Assert.True(ServerAddress.TryParse("https://storava.example/app", out var address, out _));

        Assert.Equal(
            "https://storava.example/app/api/agent/pair",
            new Uri(address, "api/agent/pair").ToString());
    }
}

/// <summary>Covers the small argument reader, including the forms a user is likely to type.</summary>
public sealed class CommandLineTests
{
    [Fact]
    public void The_first_bare_word_is_the_verb()
    {
        Assert.Equal("pair", CommandLine.Parse(["pair", "--server", "https://x"]).Verb);
        Assert.Equal("status", CommandLine.Parse(["STATUS"]).Verb);
    }

    [Fact]
    public void No_arguments_asks_for_help_rather_than_doing_something()
    {
        Assert.Equal("help", CommandLine.Parse([]).Verb);
        Assert.Equal("help", CommandLine.Parse(["--server", "https://x"]).Verb);
    }

    [Fact]
    public void An_option_can_be_written_either_way()
    {
        Assert.Equal("https://x", CommandLine.Parse(["pair", "--server", "https://x"]).Option("server"));
        Assert.Equal("https://x", CommandLine.Parse(["pair", "--server=https://x"]).Option("server"));
    }

    [Fact]
    public void A_bare_switch_reads_as_a_flag_not_as_a_value()
    {
        var command = CommandLine.Parse(["unpair", "--keep-identity"]);

        Assert.True(command.HasFlag("keep-identity"));
        Assert.Null(command.Option("keep-identity"));
    }

    [Fact]
    public void A_flag_before_an_option_does_not_swallow_it()
    {
        var command = CommandLine.Parse(["pair", "--keep-identity", "--server", "https://x"]);

        Assert.True(command.HasFlag("keep-identity"));
        Assert.Equal("https://x", command.Option("server"));
    }

    [Fact]
    public void A_missing_option_reads_as_absent_rather_than_empty()
    {
        Assert.Null(CommandLine.Parse(["pair"]).Option("code"));
        Assert.Null(CommandLine.Parse(["pair", "--code", "--name", "PC"]).Option("code"));
    }
}
