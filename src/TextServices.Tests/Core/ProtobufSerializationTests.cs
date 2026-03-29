using FluentAssertions;
using ProtoBuf;
using TextServices.Core.Models;
using TextServices.Core.Providers;

namespace TextServices.Tests.Core;

/// <summary>
/// Verifies that Text and AutoComplete survive a Protobuf serialise/deserialise round-trip.
/// This guards against accidental changes to ProtoMember field numbers.
/// </summary>
public class ProtobufSerializationTests
{
    private static TextBuildResult BuildResult()
    {
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/canvas/1");
        acc.NextLine();
        acc.AddWord("The", "the", 0, 0, 40, 20);
        acc.AddWord("quick", "quick", 50, 0, 60, 20);
        acc.AddWord("brown", "brown", 120, 0, 60, 20);
        acc.NextLine();
        acc.AddWord("fox", "fox", 0, 30, 40, 20);
        acc.BeginPage("https://example.org/canvas/2");
        acc.NextLine();
        acc.AddWord("jumps", "jumps", 0, 0, 60, 20);
        return acc.Build();
    }

    [Fact]
    public void Text_RoundTrips_ViaProtobuf()
    {
        var original = BuildResult().Text;

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var roundTripped = Serializer.Deserialize<Text>(ms);

        roundTripped.NormalisedFullText.Should().Be(original.NormalisedFullText);
        roundTripped.RawFullText.Should().Be(original.RawFullText);
        roundTripped.Words.Should().HaveCount(original.Words.Count);
        roundTripped.Images.Should().HaveCount(original.Images.Length);
    }

    [Fact]
    public void AutoComplete_RoundTrips_ViaProtobuf()
    {
        var original = BuildResult().AutoComplete;

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var roundTripped = Serializer.Deserialize<AutoComplete>(ms);

        roundTripped.Buckets.Should().HaveCount(original.Buckets.Count);

        foreach (var kvp in original.Buckets)
        {
            roundTripped.Buckets.Should().ContainKey(kvp.Key);
            roundTripped.Buckets[kvp.Key].Should().BeEquivalentTo(kvp.Value);
        }
    }

    [Fact]
    public void Text_Search_WorksAfterRoundTrip()
    {
        var original = BuildResult().Text;

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var roundTripped = Serializer.Deserialize<Text>(ms);

        var results = roundTripped.Search("quick brown");
        results.Should().HaveCount(1);
        results[0].ContentRaw.Should().Be("quick brown");
    }
}
