using Shouldly;
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
        text.Search("").ShouldBeEmpty();
        text.Search("   ").ShouldBeEmpty();
    }

    [Fact]
    public void Search_ReturnsEmpty_WhenNoMatch()
    {
        var text = BuildSimpleText("hello", "world");
        text.Search("foo").ShouldBeEmpty();
    }

    [Fact]
    public void Search_FindsSingleWord()
    {
        var text = BuildSimpleText("the", "quick", "brown", "fox");
        var results = text.Search("quick");
        results.Count.ShouldBe(1);
        results[0].ContentRaw.ShouldBe("quick");
        results[0].Hit.ShouldBe(0); // single-word: hit = element index (0-based)
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        var text = BuildSimpleText("The", "Quick", "Brown", "Fox");
        text.Search("QUICK").Count.ShouldBe(1);
        text.Search("quick").Count.ShouldBe(1);
        text.Search("Quick").Count.ShouldBe(1);
    }

    [Fact]
    public void Search_FindsMultipleOccurrences()
    {
        var text = BuildSimpleText("the", "cat", "sat", "on", "the", "mat");
        var results = text.Search("the");
        results.Count.ShouldBe(2);
        results[0].Hit.ShouldBe(0); // single-word: hit = element index (0-based)
        results[1].Hit.ShouldBe(1);
    }

    [Fact]
    public void Search_CoalescesAdjacentWordsOnSameLine()
    {
        var text = BuildSimpleText("the", "quick", "brown", "fox");
        var results = text.Search("quick brown");
        results.Count.ShouldBe(1);
        results[0].ContentRaw.ShouldBe("quick brown");
        results[0].Wds.Count.ShouldBe(2);
    }

    [Fact]
    public void Search_ResultRect_HasCorrectImageIndex()
    {
        var text = BuildSimpleText("hello", "world");
        var results = text.Search("hello");
        results[0].Idx.ShouldBe(0);
    }

    [Fact]
    public void Search_IncludesBeforeContext()
    {
        var text = BuildSimpleText("the", "quick", "brown", "fox", "jumps");
        var results = text.Search("fox");
        results.Count.ShouldBe(1);
        var before = results[0].Before;
        before.ShouldNotBeNullOrEmpty();
        before!.ShouldContain("brown");
    }

    [Fact]
    public void Search_IncludesAfterContext()
    {
        var text = BuildSimpleText("the", "quick", "brown", "fox", "jumps");
        var results = text.Search("fox");
        var after = results[0].After;
        after.ShouldNotBeNullOrEmpty();
        after!.ShouldContain("jumps");
    }

    [Fact]
    public void Search_EmptyText_ReturnsEmpty()
    {
        var text = new Text();
        text.Search("anything").ShouldBeEmpty();
    }

    [Fact]
    public void Search_FindsWordByNormalisedForm()
    {
        var text = BuildSimpleText("it's", "complicated");
        // "it's" normalises to "its" (apostrophe dropped, not replaced with space)
        text.Search("it").ShouldNotBeEmpty();
    }
}
