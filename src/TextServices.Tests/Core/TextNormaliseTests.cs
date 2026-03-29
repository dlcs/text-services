using Shouldly;
using TextServices.Core.Models;

namespace TextServices.Tests.Core;

public class TextNormaliseTests
{
    [Theory]
    [InlineData("Hello World", "hello world")]
    [InlineData("HELLO WORLD", "hello world")]
    [InlineData("hello world", "hello world")]
    public void Normalise_LowercasesInput(string input, string expected)
        => Text.Normalise(input).ShouldBe(expected);

    [Theory]
    [InlineData("hello, world!", "hello world")]
    [InlineData("it's a test.", "it s a test")]
    [InlineData("foo-bar", "foo bar")]
    [InlineData("£100", "100")]
    public void Normalise_StripsPunctuation(string input, string expected)
        => Text.Normalise(input).ShouldBe(expected);

    [Theory]
    [InlineData("hello   world", "hello world")]
    [InlineData("  hello  ", "hello")]
    [InlineData("\thello\nworld\t", "hello world")]
    public void Normalise_CollapsesWhitespace(string input, string expected)
        => Text.Normalise(input).ShouldBe(expected);

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("!!!", "")]
    public void Normalise_HandlesEdgeCases(string? input, string expected)
        => Text.Normalise(input).ShouldBe(expected);

    [Fact]
    public void Normalise_PreservesDigits()
        => Text.Normalise("page 42").ShouldBe("page 42");

    [Fact]
    public void Normalise_IsIdempotent()
    {
        var input = "Hello, World! This is a test.";
        var once = Text.Normalise(input);
        Text.Normalise(once).ShouldBe(once);
    }
}
