using FluentAssertions;
using TextServices.Core.Models;
using TextServices.Core.Providers;

namespace TextServices.Tests.Core;

/// <summary>
/// Tests for Text.Search using manually constructed Text objects
/// (the builder and ALTO parsing are tested separately in PR 2).
/// </summary>
public class TextSearchTests
{
    /// <summary>
    /// Builds a simple single-page Text with a known sequence of words,
    /// useful for testing the search algorithm directly.
    /// </summary>
    private static Text BuildSimpleText(params string[] rawWords)
    {
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/canvas/1");
        acc.NextLine();
        int x = 0;
        foreach (var word in rawWords)
        {
            var norm = Text.Normalise(word);
            if (!string.IsNullOrEmpty(norm))
            {
                acc.AddWord(word, norm, x, 0, 50, 20, spaceAfter: 10);
                x += 60;
            }
        }
        return acc.Build().Text;
    }

    [Fact]
    public void Search_ReturnsEmpty_WhenQueryIsEmpty()
    {
        var text = BuildSimpleText("hello", "world");
        text.Search("").Should().BeEmpty();
        text.Search("   ").Should().BeEmpty();
    }

    [Fact]
    public void Search_ReturnsEmpty_WhenNoMatch()
    {
        var text = BuildSimpleText("hello", "world");
        text.Search("foo").Should().BeEmpty();
    }

    [Fact]
    public void Search_FindsSingleWord()
    {
        var text = BuildSimpleText("the", "quick", "brown", "fox");
        var results = text.Search("quick");
        results.Should().HaveCount(1);
        results[0].ContentRaw.Should().Be("quick");
        results[0].Hit.Should().Be(1);
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        var text = BuildSimpleText("The", "Quick", "Brown", "Fox");
        text.Search("QUICK").Should().HaveCount(1);
        text.Search("quick").Should().HaveCount(1);
        text.Search("Quick").Should().HaveCount(1);
    }

    [Fact]
    public void Search_FindsMultipleOccurrences()
    {
        var text = BuildSimpleText("the", "cat", "sat", "on", "the", "mat");
        var results = text.Search("the");
        results.Should().HaveCount(2);
        results[0].Hit.Should().Be(1);
        results[1].Hit.Should().Be(2);
    }

    [Fact]
    public void Search_CoalescesAdjacentWordsOnSameLine()
    {
        var text = BuildSimpleText("the", "quick", "brown", "fox");
        var results = text.Search("quick brown");
        results.Should().HaveCount(1);
        results[0].ContentRaw.Should().Be("quick brown");
        results[0].Wds.Should().HaveCount(2);
    }

    [Fact]
    public void Search_ResultRect_HasCorrectImageIndex()
    {
        var text = BuildSimpleText("hello", "world");
        var results = text.Search("hello");
        results[0].Idx.Should().Be(0);
    }

    [Fact]
    public void Search_IncludesBeforeContext()
    {
        var text = BuildSimpleText("the", "quick", "brown", "fox", "jumps");
        var results = text.Search("fox");
        results.Should().HaveCount(1);
        results[0].Before.Should().NotBeNullOrEmpty();
        results[0].Before.Should().Contain("brown");
    }

    [Fact]
    public void Search_IncludesAfterContext()
    {
        var text = BuildSimpleText("the", "quick", "brown", "fox", "jumps");
        var results = text.Search("fox");
        results[0].After.Should().NotBeNullOrEmpty();
        results[0].After.Should().Contain("jumps");
    }

    [Fact]
    public void Search_EmptyText_ReturnsEmpty()
    {
        var text = new Text();
        text.Search("anything").Should().BeEmpty();
    }

    [Fact]
    public void Search_FindsWordByNormalisedForm()
    {
        var text = BuildSimpleText("it's", "complicated");
        // "it's" normalises to "it s", so searching "it" should find it
        var results = text.Search("it");
        results.Should().NotBeEmpty();
    }
}
