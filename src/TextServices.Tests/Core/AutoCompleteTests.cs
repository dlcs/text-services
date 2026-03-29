using FluentAssertions;
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
        ac.GetSuggestions("he").Should().BeEmpty();
        ac.GetSuggestions("h").Should().BeEmpty();
        ac.GetSuggestions("").Should().BeEmpty();
    }

    [Fact]
    public void GetSuggestions_ReturnsSuggestions_ForThreeCharPrefix()
    {
        var ac = BuildAutoComplete("hello", "help", "helmet", "world");
        var suggestions = ac.GetSuggestions("hel");
        suggestions.Should().Contain("hello");
        suggestions.Should().Contain("help");
        suggestions.Should().Contain("helmet");
        suggestions.Should().NotContain("world");
    }

    [Fact]
    public void GetSuggestions_FiltersByFullPrefix()
    {
        var ac = BuildAutoComplete("hello", "help", "helicopter", "world");
        var suggestions = ac.GetSuggestions("help");
        suggestions.Should().Contain("help");
        suggestions.Should().NotContain("hello");
    }

    [Fact]
    public void GetSuggestions_IsCaseInsensitive()
    {
        var ac = BuildAutoComplete("Hello", "HELP", "world");
        ac.GetSuggestions("HEL").Should().Contain("hello");
        ac.GetSuggestions("hel").Should().Contain("help");
    }

    [Fact]
    public void GetSuggestions_OrdersByLengthThenAlphabetically()
    {
        var ac = BuildAutoComplete("helicopter", "help", "hello");
        var suggestions = ac.GetSuggestions("hel");
        suggestions.Should().ContainInOrder("help", "hello", "helicopter");
    }

    [Fact]
    public void GetSuggestions_ReturnsEmpty_ForNoMatch()
    {
        var ac = BuildAutoComplete("hello", "world");
        ac.GetSuggestions("xyz").Should().BeEmpty();
    }

    [Fact]
    public void IsEmpty_TrueForEmptyAccumulator()
    {
        var ac = new AutoComplete();
        ac.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ShortWords_NotIndexed()
    {
        // Words shorter than 3 chars should not appear in autocomplete buckets.
        var ac = BuildAutoComplete("a", "to", "the", "quick");
        ac.GetSuggestions("the").Should().Contain("the");
        // "a" and "to" are too short to be indexed
        ac.Buckets.Should().NotContainKey("a");
        ac.Buckets.Should().NotContainKey("to");
    }
}
