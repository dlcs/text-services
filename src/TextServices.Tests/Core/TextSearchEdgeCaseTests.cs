using Shouldly;
using TextServices.Core.Models;
using TextServices.Core.Providers;

namespace TextServices.Tests.Core;

/// <summary>
/// Search correctness tests over longer, more realistic text passages.
/// Covers the key "maximise hit chance" scenarios: punctuation in the stored word,
/// punctuation in the query, substring matching, multi-word phrases, and document
/// boundary behaviour.
/// </summary>
public class TextSearchEdgeCaseTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a single-page Text from a sequence of raw word tokens,
    /// exactly as they would come from an ALTO String/@CONTENT attribute.
    /// Each token is normalised individually.
    /// </summary>
    private static Text BuildFromTokens(params string[] rawWords)
    {
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/canvas/1");
        acc.NextLine();
        int x = 0;
        foreach (var raw in rawWords)
        {
            var norm = Text.Normalise(raw);
            if (!string.IsNullOrEmpty(norm))
            {
                acc.AddWord(raw, norm, x, 0, 50, 20, spaceAfter: 5);
                x += 60;
            }
            // Words that normalise to empty (e.g. "---") are silently skipped,
            // exactly as the builder will behave with punctuation-only ALTO tokens.
        }
        return acc.Build().Text;
    }

    /// <summary>
    /// Builds a two-page Text. Words on page 2 are on a different canvas.
    /// </summary>
    private static Text BuildTwoPage(string[] page1Words, string[] page2Words)
    {
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/canvas/1");
        acc.NextLine();
        int x = 0;
        foreach (var raw in page1Words)
        {
            var norm = Text.Normalise(raw);
            if (!string.IsNullOrEmpty(norm)) { acc.AddWord(raw, norm, x, 0, 50, 20); x += 60; }
        }
        acc.BeginPage("https://example.org/canvas/2");
        acc.NextLine();
        x = 0;
        foreach (var raw in page2Words)
        {
            var norm = Text.Normalise(raw);
            if (!string.IsNullOrEmpty(norm)) { acc.AddWord(raw, norm, x, 0, 50, 20); x += 60; }
        }
        return acc.Build().Text;
    }

    // -------------------------------------------------------------------------
    // Query/text symmetry: punctuation differences cancel out
    // -------------------------------------------------------------------------

    [Fact]
    public void Search_QueryWithPunctuation_FindsWordWithPunctuation()
    {
        // Text contains "don't" (ALTO token with apostrophe).
        // Both text and query normalise to "dont", so they match.
        var text = BuildFromTokens("he", "said", "don't", "go");
        text.Search("don't").Count.ShouldBe(1);
        text.Search("dont").Count.ShouldBe(1);
    }

    [Fact]
    public void Search_HyphenatedQuery_FindsHyphenatedWord()
    {
        var text = BuildFromTokens("a", "well-known", "fact");
        // Stored: "wellknown"; both query forms normalise to "wellknown"
        text.Search("well-known").Count.ShouldBe(1);
        text.Search("wellknown").Count.ShouldBe(1);
        text.Search("well known").Count.ShouldBe(0); // "well known" ≠ "wellknown"
    }

    [Fact]
    public void Search_NumberWithSeparators_FindsNumber()
    {
        var text = BuildFromTokens("population", "of", "1,250,000", "people");
        // "1,250,000" normalises to "1250000"
        text.Search("1250000").Count.ShouldBe(1);
        text.Search("1,250,000").Count.ShouldBe(1);
    }

    [Fact]
    public void Search_Abbreviation_Found()
    {
        var text = BuildFromTokens("Mr.", "Smith", "arrived");
        text.Search("Mr.").Count.ShouldBe(1);
        text.Search("mr").Count.ShouldBe(1);
        text.Search("Mr").Count.ShouldBe(1);
    }

    [Fact]
    public void Search_CurlyApostrophe_FindsSameAsAsciiApostrophe()
    {
        // OCR may produce curly apostrophes (U+2019); search with straight (U+0027) must still hit.
        var text = BuildFromTokens("it\u2019s", "complicated");  // curly apostrophe in stored word
        text.Search("its").Count.ShouldBe(1);
        text.Search("it's").Count.ShouldBe(1);   // ASCII apostrophe in query
        text.Search("it\u2019s").Count.ShouldBe(1); // curly apostrophe in query
    }

    // -------------------------------------------------------------------------
    // Substring matching — the reference finds substrings within words,
    // and expands to capture the whole containing word
    // -------------------------------------------------------------------------

    [Fact]
    public void Search_SubstringWithinWord_CapturesWholeWord()
    {
        var text = BuildFromTokens("the", "category", "list");
        var results = text.Search("cat");
        results.Count.ShouldBe(1);
        // The whole word "category" is captured, not just "cat"
        results[0].ContentRaw.ShouldBe("category");
    }

    [Fact]
    public void Search_SubstringAtStart_CapturesWholeWord()
    {
        var text = BuildFromTokens("pre", "prefix", "word");
        var results = text.Search("pre");
        // Finds both "pre" and "prefix" (both contain "pre" after normalisation)
        results.Count.ShouldBe(2);
    }

    // -------------------------------------------------------------------------
    // Multi-word phrases over longer passages
    // -------------------------------------------------------------------------

    [Fact]
    public void Search_ThreeWordPhrase_FoundInMiddleOfPassage()
    {
        var text = BuildFromTokens(
            "the", "report", "published", "by", "the", "Royal", "Commission",
            "on", "the", "state", "of", "large", "towns", "and", "populous",
            "districts", "in", "1844", "showed", "that", "sanitary", "conditions",
            "were", "poor"
        );
        var results = text.Search("large towns and");
        results.Count.ShouldBe(1);
        // Three words coalesced into one rect (all on same line)
        results[0].ContentRaw.ShouldBe("large towns and");
    }

    [Fact]
    public void Search_FiveWordPhrase_Found()
    {
        var text = BuildFromTokens(
            "it", "is", "a", "truth", "universally", "acknowledged", "that",
            "a", "single", "man", "in", "possession", "of", "a", "good",
            "fortune", "must", "be", "in", "want", "of", "a", "wife"
        );
        var results = text.Search("single man in possession of");
        results.Count.ShouldBe(1);
    }

    [Fact]
    public void Search_PassageWithPunctuation_PhraseFound()
    {
        // Realistic ALTO tokens with punctuation attached
        var text = BuildFromTokens(
            "Mr.", "Smith's", "report,", "published", "in", "1842,",
            "showed", "that", "1,250", "people", "were", "affected."
        );
        // "Mr." → "mr", "Smith's" → "smiths", "report," → "report"
        // "1842," → "1842", "1,250" → "1250", "affected." → "affected"
        text.Search("mr").Count.ShouldBe(1);
        text.Search("smiths").Count.ShouldBe(1);
        text.Search("1842").Count.ShouldBe(1);
        text.Search("1250").Count.ShouldBe(1);
        text.Search("affected").Count.ShouldBe(1);
        // Phrase crossing punctuation-bearing tokens
        text.Search("report published in 1842").Count.ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Repeated words — correct hit count
    // -------------------------------------------------------------------------

    [Fact]
    public void Search_WordRepeatedManyTimes_CorrectCount()
    {
        var text = BuildFromTokens(
            "the", "cat", "sat", "on", "the", "mat", "and", "the",
            "cat", "wore", "a", "hat", "near", "the", "flat"
        );
        // "the" appears 4 times
        text.Search("the").Count.ShouldBe(4);
        // "cat" appears 2 times
        text.Search("cat").Count.ShouldBe(2);
    }

    [Fact]
    public void Search_TwoWordPhraseRepeated_CorrectCount()
    {
        var text = BuildFromTokens(
            "to", "be", "or", "not", "to", "be", "that", "is", "the", "question"
        );
        text.Search("to be").Count.ShouldBe(2);
    }

    // -------------------------------------------------------------------------
    // Document boundary behaviour
    // -------------------------------------------------------------------------

    [Fact]
    public void Search_FirstWord_Found()
    {
        var text = BuildFromTokens("parliament", "met", "in", "october");
        var results = text.Search("parliament");
        results.Count.ShouldBe(1);
        results[0].Idx.ShouldBe(0);
    }

    [Fact]
    public void Search_LastWord_Found()
    {
        var text = BuildFromTokens("met", "in", "october");
        text.Search("october").Count.ShouldBe(1);
    }

    [Fact]
    public void Search_SingleWordDocument_Found()
    {
        var text = BuildFromTokens("parliament");
        text.Search("parliament").Count.ShouldBe(1);
        text.Search("parlia").Count.ShouldBe(1); // substring
        text.Search("xyz").Count.ShouldBe(0);
    }

    // -------------------------------------------------------------------------
    // Multi-page: words at a page boundary should NOT be coalesced
    // -------------------------------------------------------------------------

    [Fact]
    public void Search_WordsAtPageBoundary_NotCoalesced()
    {
        // "fox" ends page 1; "jumps" starts page 2
        var text = BuildTwoPage(
            ["the", "quick", "brown", "fox"],
            ["jumps", "over", "the", "lazy", "dog"]
        );

        // "fox jumps" spans two pages — the words are found but should NOT be
        // coalesced (they're on different canvases with different Li values)
        var results = text.Search("fox jumps");
        results.Count.ShouldBe(2); // two separate rects, same hit number
        results[0].Hit.ShouldBe(results[1].Hit); // same hit
        results[0].Idx.ShouldBe(0); // page 1
        results[1].Idx.ShouldBe(1); // page 2
    }

    // -------------------------------------------------------------------------
    // Words that normalise to empty are skipped — no phantom hits
    // -------------------------------------------------------------------------

    [Fact]
    public void Search_PunctuationOnlyTokensSkipped_NoGapsInResults()
    {
        // Some ALTO files contain String elements whose CONTENT is pure punctuation
        // (e.g. a standalone "—" or "..."). These should not create phantom words.
        var text = BuildFromTokens("hello", "---", "world"); // "---" normalises to empty → skipped
        var results = text.Search("hello world");
        // "hello" and "world" are NOT adjacent (the skipped token breaks the word chain)
        // so they won't coalesce. But both should be found individually.
        text.Search("hello").Count.ShouldBe(1);
        text.Search("world").Count.ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Numbers and years
    // -------------------------------------------------------------------------

    [Fact]
    public void Search_YearInText_Found()
    {
        var text = BuildFromTokens("in", "the", "year", "1842", "the", "act", "was", "passed");
        text.Search("1842").Count.ShouldBe(1);
    }

    [Fact]
    public void Search_NumberWithFormattingInBothTextAndQuery()
    {
        var text = BuildFromTokens("some", "1,250,000", "people");
        // Text token normalises to "1250000"; query normalises to same
        text.Search("1,250,000").Count.ShouldBe(1);
        text.Search("1250000").Count.ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Context: Before/After are character-based snippets from raw text
    // -------------------------------------------------------------------------

    [Fact]
    public void Search_Context_ContainsRawText_WithPunctuation()
    {
        var text = BuildFromTokens(
            "the", "report,", "published", "in", "London,", "showed",
            "that", "conditions", "were", "poor."
        );
        var results = text.Search("london");
        results.Count.ShouldBe(1);
        // The context should come from the RawFullText which preserves the comma
        results[0].Before.ShouldNotBeNull();
        results[0].Before!.ShouldContain("in");    // raw "in" precedes "London,"
    }

    [Fact]
    public void Search_Context_EmptyAtStartOfDocument()
    {
        var text = BuildFromTokens("parliament", "met", "in", "october");
        var results = text.Search("parliament");
        results.Count.ShouldBe(1);
        results[0].Before.ShouldBe(string.Empty); // nothing before the first word
    }

    [Fact]
    public void Search_Context_EmptyAtEndOfDocument()
    {
        var text = BuildFromTokens("the", "act", "was", "passed");
        var results = text.Search("passed");
        results.Count.ShouldBe(1);
        results[0].After.ShouldBe(string.Empty); // nothing after the last word
    }

    // -------------------------------------------------------------------------
    // Empty / degenerate inputs
    // -------------------------------------------------------------------------

    [Fact]
    public void Search_PunctuationOnlyQuery_ReturnsEmpty()
    {
        var text = BuildFromTokens("hello", "world");
        // "---" normalises to "" → empty query → no results
        text.Search("---").ShouldBeEmpty();
        text.Search("...").ShouldBeEmpty();
        text.Search("£$%").ShouldBeEmpty();
    }

    [Fact]
    public void Search_EmptyDocument_ReturnsEmpty()
    {
        // A document with only punctuation-only tokens produces an empty Text
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/canvas/1");
        acc.NextLine();
        acc.AddWord("---", "", 0, 0, 50, 20); // norm is empty → skipped
        var result = acc.Build();
        result.IsEmpty.ShouldBeTrue();
        result.Text.Search("anything").ShouldBeEmpty();
    }
}
