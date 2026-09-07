using CW4Archipelago.Core;
using Xunit;

namespace CW4Archipelago.Core.Tests;

/// <summary>
/// The panel writes what it parses into the config on connect, so a parse that
/// yields nothing is a config that says nothing - and the field's placeholder
/// keeps showing the default, which makes it look set.
/// </summary>
public class ServerAddressTests
{
    [Theory]
    [InlineData("archipelago.gg:38281", "archipelago.gg", 38281)]
    [InlineData("archipelago.gg:49229", "archipelago.gg", 49229)]
    [InlineData("localhost:38401", "localhost", 38401)]
    public void HostAndPortSplitOnTheLastColon(string text, string host, int port)
    {
        Assert.Equal(host, ServerAddress.Host(text));
        Assert.Equal(port, ServerAddress.Port(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyFallsBackToTheDefaults(string? text)
    {
        // THE BUG: this used to give an empty host, which then got saved.
        Assert.Equal("archipelago.gg", ServerAddress.Host(text));
        Assert.Equal(38281, ServerAddress.Port(text));
    }

    [Fact]
    public void AHostWithNoPortKeepsTheDefaultPort()
    {
        Assert.Equal("archipelago.gg", ServerAddress.Host("archipelago.gg"));
        Assert.Equal(38281, ServerAddress.Port("archipelago.gg"));
    }

    [Theory]
    [InlineData("archipelago.gg:")]
    [InlineData("archipelago.gg:abc")]
    [InlineData("archipelago.gg:0")]
    [InlineData("archipelago.gg:99999")]
    public void AnUnusablePortFallsBackWithoutLosingTheHost(string text)
    {
        Assert.Equal("archipelago.gg", ServerAddress.Host(text));
        Assert.Equal(38281, ServerAddress.Port(text));
    }

    [Fact]
    public void AnIpv6LiteralSplitsOnItsLastColon()
    {
        // Not a supported setup, but the parser must not mangle it into a port.
        Assert.Equal("[::1]", ServerAddress.Host("[::1]:38281"));
        Assert.Equal(38281, ServerAddress.Port("[::1]:38281"));
    }

    [Fact]
    public void SurroundingSpaceIsIgnored()
    {
        Assert.Equal("archipelago.gg", ServerAddress.Host("  archipelago.gg:38281  "));
        Assert.Equal(38281, ServerAddress.Port("  archipelago.gg:38281  "));
    }
}
