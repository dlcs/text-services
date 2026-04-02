using Shouldly;
using TextServices.Core.Models;
using TextServices.Core.Providers;

namespace TextServices.Tests.Core;

/// <summary>
/// Tests for <see cref="VttTextFormatProvider"/> via <see cref="TextBuilder.AddTranscriptPage"/>.
/// </summary>
public class VttParsingTests
{
    private readonly VttTextFormatProvider _provider = new();

    // -------------------------------------------------------------------------
    // Supports() detection
    // -------------------------------------------------------------------------

    [Fact]
    public void Supports_VttFormat_ReturnsTrue()
    {
        _provider.Supports(profile: null, format: "text/vtt", label: null).ShouldBeTrue();
    }

    [Fact]
    public void Supports_VttLabel_ReturnsTrue()
    {
        _provider.Supports(profile: null, format: null, label: "WebVTT").ShouldBeTrue();
    }

    [Fact]
    public void Supports_TranscriptLabel_ReturnsTrue()
    {
        _provider.Supports(profile: null, format: null, label: "Transcript").ShouldBeTrue();
    }

    [Fact]
    public void Supports_AltoProfile_ReturnsFalse()
    {
        _provider.Supports(profile: "http://www.loc.gov/standards/alto/ns-v3#", format: null, label: null).ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Basic cue parsing
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseBasicCue_WordsHaveCorrectTimestamps()
    {
        var vtt = MakeVtt("Hello world");
        var result = BuildFromVtt("https://example.org/c/1", vtt);

        var words = result.Text.Words.Values.ToList();
        words.Count.ShouldBe(2);
        words[0].StartMs.ShouldBe(5000);
        words[0].EndMs.ShouldBe(8500);
        words[1].StartMs.ShouldBe(5000);
        words[1].EndMs.ShouldBe(8500);
    }

    [Fact]
    public void ParseBasicCue_WordsHaveZeroXYWH()
    {
        var vtt = MakeVtt("Hello world");
        var result = BuildFromVtt("https://example.org/c/1", vtt);

        var words = result.Text.Words.Values.ToList();
        words[0].X.ShouldBe(0);
        words[0].Y.ShouldBe(0);
        words[0].W.ShouldBe(0);
        words[0].H.ShouldBe(0);
    }

    [Fact]
    public void ParseMultipleCues_WordsGetDifferentLineNumbers()
    {
        var vtt = MakeVtt("Hello world", "foo bar");
        var result = BuildFromVtt("https://example.org/c/1", vtt);

        var words = result.Text.Words.Values.OrderBy(w => w.Wd).ToList();
        var cue1Li = words[0].Li;
        var cue2Li = words[2].Li;   // first word of second cue

        cue2Li.ShouldBeGreaterThan(cue1Li);
    }

    [Fact]
    public void ParseMultipleCues_EachCueHasOwnTimestamp()
    {
        var vtt = MakeVtt("Hello world", "foo bar");
        var result = BuildFromVtt("https://example.org/c/1", vtt);

        var words = result.Text.Words.Values.OrderBy(w => w.Wd).ToList();

        words[0].StartMs.ShouldBe(5000);
        words[0].EndMs.ShouldBe(8500);
        words[1].StartMs.ShouldBe(5000);
        words[1].EndMs.ShouldBe(8500);

        words[2].StartMs.ShouldBe(10000);
        words[2].EndMs.ShouldBe(15000);
        words[3].StartMs.ShouldBe(10000);
        words[3].EndMs.ShouldBe(15000);
    }

    [Fact]
    public void ParseCueWithSpeakerTag_TagStripped()
    {
        var vtt = """
            WEBVTT

            00:00:05.000 --> 00:00:08.500
            <v Alice>Hello there</v>

            """;

        var result = BuildFromVtt("https://example.org/c/1", vtt);

        var words = result.Text.Words.Values.OrderBy(w => w.Wd).ToList();
        words.Count.ShouldBe(2);
        words[0].ContentRaw.ShouldBe("Hello");
        words[1].ContentRaw.ShouldBe("there");
    }

    [Fact]
    public void ParseCueWithUuidCueId_UuidNotIndexed()
    {
        var vtt = """
            WEBVTT

            550e8400-e29b-41d4-a716-446655440000
            00:00:05.000 --> 00:00:08.500
            Hello world

            """;

        var result = BuildFromVtt("https://example.org/c/1", vtt);

        var words = result.Text.Words.Values.OrderBy(w => w.Wd).ToList();
        words.Count.ShouldBe(2);
        words[0].ContentRaw.ShouldBe("Hello");
        words[1].ContentRaw.ShouldBe("world");

        // UUID should not appear in the indexed text
        result.Text.RawFullText.ShouldNotContain("550e8400");
    }

    [Fact]
    public void ParseMultiLineCue_AllLinesIndexed()
    {
        var vtt = """
            WEBVTT

            00:00:05.000 --> 00:00:08.500
            Hello world
            foo bar

            """;

        var result = BuildFromVtt("https://example.org/c/1", vtt);

        var words = result.Text.Words.Values.OrderBy(w => w.Wd).ToList();
        words.Count.ShouldBe(4);
        words[0].ContentRaw.ShouldBe("Hello");
        words[1].ContentRaw.ShouldBe("world");
        words[2].ContentRaw.ShouldBe("foo");
        words[3].ContentRaw.ShouldBe("bar");
    }

    [Fact]
    public void ParseCue_MmSsFormat_TimestampParsedCorrectly()
    {
        var vtt = """
            WEBVTT

            01:30.500 --> 02:00.000
            Hello world

            """;

        var result = BuildFromVtt("https://example.org/c/1", vtt);

        var words = result.Text.Words.Values.ToList();
        words[0].StartMs.ShouldBe(90500);
        words[0].EndMs.ShouldBe(120000);
    }

    // -------------------------------------------------------------------------
    // Image.IsTemporalContent
    // -------------------------------------------------------------------------

    [Fact]
    public void Image_IsTemporalContent_True()
    {
        var vtt = MakeVtt("Hello world");
        var result = BuildFromVtt("https://example.org/c/1", vtt);

        result.Text.Images.ShouldNotBeEmpty();
        result.Text.Images[0].IsTemporalContent.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Search integration
    // -------------------------------------------------------------------------

    [Fact]
    public void TemporalText_SearchFindsWord()
    {
        var vtt = MakeVtt("Hello world");
        var text = BuildFromVtt("https://example.org/c/1", vtt).Text;

        var results = text.Search("hello");

        results.Count.ShouldBe(1);
        results[0].StartMs.ShouldBe(5000);
        results[0].EndMs.ShouldBe(8500);
    }

    [Fact]
    public void TemporalText_MultiWordQuery_CoalescedCorrectly()
    {
        var vtt = MakeVtt("Hello world");
        var text = BuildFromVtt("https://example.org/c/1", vtt).Text;

        var results = text.Search("hello world");

        // Two adjacent words in same cue → single ResultRect
        results.Count.ShouldBe(1);
        results[0].StartMs.ShouldBe(5000);
        results[0].EndMs.ShouldBe(8500);
        results[0].ContentRaw.ShouldBe("Hello world");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a VTT string with the fixed two-cue timing pattern.
    /// If only one cue text is supplied the second cue line is omitted.
    /// </summary>
    private static string MakeVtt(string cue1Text, string? cue2Text = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("WEBVTT");
        sb.AppendLine();
        sb.AppendLine("00:00:05.000 --> 00:00:08.500");
        sb.AppendLine(cue1Text);
        if (cue2Text != null)
        {
            sb.AppendLine();
            sb.AppendLine("00:00:10.000 --> 00:00:15.000");
            sb.AppendLine(cue2Text);
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static TextBuildResult BuildFromVtt(string canvasId, string vtt)
    {
        var builder = new TextBuilder();
        builder.AddTranscriptPage(canvasId, 0, 0, vtt, format: "text/vtt");
        return builder.Build();
    }
}
