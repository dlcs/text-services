using System.Xml.Linq;
using Shouldly;
using TextServices.Core.Providers;

namespace TextServices.Tests.Core;

/// <summary>
/// Tests for what <see cref="TextBuilder"/> reports about each page it's given.
/// </summary>
public class TextBuilderTests
{
    private static readonly XNamespace Ns = "http://www.loc.gov/standards/alto/ns-v3#";

    private static XElement Alto(params string[] words) =>
        new(Ns + "alto",
            new XElement(Ns + "Layout",
                new XElement(Ns + "Page",
                    new XAttribute("WIDTH", 1000),
                    new XAttribute("HEIGHT", 1000),
                    new XElement(Ns + "PrintSpace",
                        new XElement(Ns + "TextBlock",
                            new XElement(Ns + "TextLine",
                                words.Select((w, i) => new XElement(Ns + "String",
                                    new XAttribute("CONTENT", w),
                                    new XAttribute("HPOS", i * 60),
                                    new XAttribute("VPOS", 10),
                                    new XAttribute("WIDTH", 50),
                                    new XAttribute("HEIGHT", 20))).ToArray<object>()))))));

    private const string Vtt = """
        WEBVTT

        00:00:01.000 --> 00:00:04.000
        hello there
        """;

    [Fact]
    public void AddPage_ReturnsTrue_AndCountsWords_WhenFormatSupported()
    {
        var builder = new TextBuilder();

        builder.AddPage("https://example.org/c/1", 1000, 1000, Alto("one", "two")).ShouldBeTrue();
        builder.AddPage("https://example.org/c/2", 1000, 1000, Alto("three")).ShouldBeTrue();

        builder.WordCount.ShouldBe(3);
    }

    [Fact]
    public void AddPage_ReturnsTrue_WhenNoRoot()
    {
        new TextBuilder().AddPage("https://example.org/c/1", 1000, 1000, null).ShouldBeTrue();
    }

    [Fact]
    public void AddPage_ReturnsFalse_WhenFormatUnsupported()
    {
        var builder = new TextBuilder();

        builder.AddPage("https://example.org/c/1", 1000, 1000, Alto("ignored"),
            profile: "https://example.org/unknown-format").ShouldBeFalse();

        builder.WordCount.ShouldBe(0);
    }

    [Fact]
    public void AddTranscriptPage_ReturnsTrue_AndCountsWords_WhenFormatSupported()
    {
        var builder = new TextBuilder();

        builder.AddTranscriptPage("https://example.org/c/1", 0, 0, Vtt, format: "text/vtt").ShouldBeTrue();

        builder.WordCount.ShouldBe(2);
    }

    [Fact]
    public void AddTranscriptPage_ReturnsTrue_WhenNoContent()
    {
        new TextBuilder().AddTranscriptPage("https://example.org/c/1", 0, 0, null, format: "text/vtt")
            .ShouldBeTrue();
    }

    [Fact]
    public void AddTranscriptPage_ReturnsFalse_WhenFormatUnsupported()
    {
        new TextBuilder().AddTranscriptPage("https://example.org/c/1", 0, 0, Vtt, format: "text/unknown")
            .ShouldBeFalse();
    }
}
