using Shouldly;
using TextServices.Core.Models;
using TextServices.Core.Providers;

namespace TextServices.Tests.Core;

public class AutoCompleteTests
{
    private static AutoComplete BuildAutoComplete(params string[] rawWords)
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
                acc.AddWord(word, norm, x, 0, 50, 20);
                x += 60;
            }
        }
        return acc.Build().AutoComplete;
    }

    [Fact]
    public void GetSuggestions_ReturnsEmpty_WhenTermTooShort()
    {
        var ac = BuildAutoComplete("hello", "world");
        ac.GetSuggestions("he").ShouldBeEmpty();
        ac.GetSuggestions("h").ShouldBeEmpty();
        ac.GetSuggestions("").ShouldBeEmpty();
    }

    [Fact]
    public void GetSuggestions_ReturnsSuggestions_ForThreeCharPrefix()
    {
        var ac = BuildAutoComplete("hello", "help", "helmet", "world");
        var suggestions = ac.GetSuggestions("hel");
        suggestions.ShouldContain("hello");
        suggestions.ShouldContain("help");
        suggestions.ShouldContain("helmet");
        suggestions.ShouldNotContain("world");
    }

    [Fact]
    public void GetSuggestions_FiltersByFullPrefix()
    {
        var ac = BuildAutoComplete("hello", "help", "helicopter", "world");
        var suggestions = ac.GetSuggestions("help");
        suggestions.ShouldContain("help");
        suggestions.ShouldNotContain("hello");
    }

    [Fact]
    public void GetSuggestions_IsCaseInsensitive()
    {
        var ac = BuildAutoComplete("Hello", "HELP", "world");
        ac.GetSuggestions("HEL").ShouldContain("hello");
        ac.GetSuggestions("hel").ShouldContain("help");
    }

    [Fact]
    public void GetSuggestions_OrdersByLengthThenAlphabetically()
    {
        var ac = BuildAutoComplete("helicopter", "help", "hello");
        var suggestions = ac.GetSuggestions("hel");
        suggestions.ShouldBe(["help", "hello", "helicopter"]);
    }

    [Fact]
    public void GetSuggestions_ReturnsEmpty_ForNoMatch()
    {
        var ac = BuildAutoComplete("hello", "world");
        ac.GetSuggestions("xyz").ShouldBeEmpty();
    }

    [Fact]
    public void IsEmpty_TrueForEmptyAccumulator()
    {
        var ac = new AutoComplete();
        ac.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void ShortWords_NotIndexed()
    {
        // Words shorter than 3 chars should not appear in autocomplete buckets.
        var ac = BuildAutoComplete("a", "to", "the", "quick");
        ac.GetSuggestions("the").ShouldContain("the");
        ac.Buckets.ShouldNotContainKey("a");
        ac.Buckets.ShouldNotContainKey("to");
    }
}
