using CW4Archipelago.Core;
using Xunit;

/// <summary>
/// The mod and the apworld ship as a matched pair, and until 2026-09-17 nothing
/// checked that at runtime. These pin the two things that make the check usable
/// rather than annoying: it must be SILENT for every seed generated before the
/// version key existed, and it must not refuse a connection.
/// </summary>
public class VersionRulesTests
{
    [Fact]
    public void MatchingVersionsSayNothing()
    {
        Assert.Null(VersionRules.Describe("0.2.0", "0.2.0"));
    }

    [Fact]
    public void AnOlderSeedWithNoVersionIsNotAMismatch()
    {
        // Every seed in flight predates the key. If this warned, it would warn
        // for all of them, which would teach people to ignore the warning -
        // the failure mode that makes a check worthless.
        Assert.Null(VersionRules.Describe("0.2.0", ""));
        Assert.Null(VersionRules.Describe("0.2.0", "   "));
        Assert.Null(VersionRules.Describe("0.2.0", null!));
    }

    [Fact]
    public void DifferentVersionsAreReported_AndBothNumbersAppear()
    {
        var note = VersionRules.Describe("0.1.5", "0.2.0");
        Assert.NotNull(note);
        // A message that does not name BOTH numbers cannot be acted on.
        Assert.Contains("0.1.5", note);
        Assert.Contains("0.2.0", note);
    }

    [Fact]
    public void WhitespaceDoesNotManufactureAMismatch()
    {
        Assert.Null(VersionRules.Describe(" 0.2.0 ", "0.2.0\n"));
    }

    [Fact]
    public void AnUnknownModVersionIsSilentRatherThanNoisy()
    {
        // Plugin.Version is a compile-time constant so this should not happen;
        // if it ever does, saying nothing beats claiming a mismatch we cannot
        // substantiate.
        Assert.Null(VersionRules.Describe("", "0.2.0"));
    }
}
