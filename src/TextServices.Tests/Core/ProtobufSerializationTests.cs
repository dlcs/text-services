using ProtoBuf;
using Shouldly;
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

        roundTripped.NormalisedFullText.ShouldBe(original.NormalisedFullText);
        roundTripped.RawFullText.ShouldBe(original.RawFullText);
        roundTripped.Words.Count.ShouldBe(original.Words.Count);
        roundTripped.Images.Length.ShouldBe(original.Images.Length);
    }

    [Fact]
    public void AutoComplete_RoundTrips_ViaProtobuf()
    {
        var original = BuildResult().AutoComplete;

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var roundTripped = Serializer.Deserialize<AutoComplete>(ms);

        roundTripped.Buckets.Count.ShouldBe(original.Buckets.Count);
        foreach (var kvp in original.Buckets)
        {
            roundTripped.Buckets.ShouldContainKey(kvp.Key);
            roundTripped.Buckets[kvp.Key].ShouldBe(kvp.Value, ignoreOrder: true);
        }
    }

    [Fact]
    public void TemporalWord_StartEndMs_RoundTrip()
    {
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/canvas/1", isTemporalContent: true);
        acc.NextLine();
        acc.AddWord("Hello", "hello", startMs: 5000, endMs: 8500);
        acc.AddWord("world", "world", startMs: 5000, endMs: 8500);
        var original = acc.Build().Text;

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var roundTripped = Serializer.Deserialize<Text>(ms);

        var words = roundTripped.Words.Values.OrderBy(w => w.Wd).ToList();
        words[0].StartMs.ShouldBe(5000);
        words[0].EndMs.ShouldBe(8500);
        words[1].StartMs.ShouldBe(5000);
        words[1].EndMs.ShouldBe(8500);
    }

    [Fact]
    public void Image_IsTemporalContent_RoundTrip()
    {
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/canvas/1", isTemporalContent: true);
        acc.NextLine();
        acc.AddWord("Hello", "hello", startMs: 1000, endMs: 2000);
        var original = acc.Build().Text;

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        ms.Position = 0;
        var roundTripped = Serializer.Deserialize<Text>(ms);

        roundTripped.Images.Length.ShouldBe(1);
        roundTripped.Images[0].IsTemporalContent.ShouldBeTrue();
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
        results.Count.ShouldBe(1);
        results[0].ContentRaw.ShouldBe("quick brown");
    }
}
