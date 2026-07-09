using Shouldly;
using TextServices.Core.Models;

namespace TextServices.Tests.Core;

/// <summary>
/// Thorough tests for Text.Normalise covering the full range of characters that appear
/// in OCR'd historical documents and ALTO files. The normalisation exists to maximise
/// the chance of a search hit, so any unexpected output here is a real defect.
///
/// Key rule: non-alphanumeric, non-whitespace characters are DROPPED (not replaced with
/// spaces). Whitespace is preserved but collapsed to a single space.
/// </summary>
public class TextNormalisationEdgeCaseTests
{
    // -------------------------------------------------------------------------
    // Contractions and apostrophes
    // The apostrophe is dropped, joining the parts: "don't" → "dont"
    // This is correct: the user searching "dont" or "don't" both normalise to "dont"
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("don't", "dont")]
    [InlineData("can't", "cant")]
    [InlineData("won't", "wont")]
    [InlineData("it's", "its")]
    [InlineData("I'm", "im")]
    [InlineData("they've", "theyve")]
    [InlineData("she'd", "shed")]
    [InlineData("we're", "were")]
    [InlineData("o'clock", "oclock")]
    [InlineData("O'Brien", "obrien")]
    public void Normalise_Contractions_ApostropheDropped(string input, string expected)
        => Text.Normalise(input).ShouldBe(expected);

    // Curly / typographic apostrophe (U+2019 RIGHT SINGLE QUOTATION MARK) —
    // common in typeset books scanned by OCR
    [Theory]
    [InlineData("don\u2019t", "dont")]
    [InlineData("it\u2019s", "its")]
    [InlineData("O\u2019Brien", "obrien")]
    public void Normalise_CurlyApostrophe_AlsoDropped(string input, string expected)
        => Text.Normalise(input).ShouldBe(expected);

    // -------------------------------------------------------------------------
    // Hyphens and dashes — all are dropped, concatenating the parts
    // "well-known" → "wellknown", "COVID-19" → "covid19"
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("well-known", "wellknown")]
    [InlineData("up-to-date", "uptodate")]
    [InlineData("state-of-the-art", "stateoftheart")]
    [InlineData("e-mail", "email")]
    [InlineData("co-op", "coop")]
    [InlineData("COVID-19", "covid19")]
    [InlineData("H2O", "h2o")]
    [InlineData("21st-century", "21stcentury")]
    public void Normalise_Hyphens_Dropped_PartsJoined(string input, string expected)
        => Text.Normalise(input).ShouldBe(expected);

    // En dash (U+2013) and em dash (U+2014) — common in typeset documents
    [Theory]
    [InlineData("word\u2013word", "wordword")]   // en dash
    [InlineData("word\u2014word", "wordword")]   // em dash
    [InlineData("1939\u20131945", "19391945")]   // date range with en dash
    public void Normalise_EnAndEmDash_Dropped(string input, string expected)
        => Text.Normalise(input).ShouldBe(expected);

    // -------------------------------------------------------------------------
    // Abbreviations and titles — the period is dropped
    // "Mr." → "mr", "U.S.A." → "usa"
    // Consequence: searching "mr" finds "Mr." ✓
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Mr.", "mr")]
    [InlineData("Dr.", "dr")]
    [InlineData("St.", "st")]
    [InlineData("etc.", "etc")]
    [InlineData("U.S.A.", "usa")]
    [InlineData("ibid.", "ibid")]
    [InlineData("vol.", "vol")]
    public void Normalise_Abbreviations_PeriodDropped(string input, string expected)
        => Text.Normalise(input).ShouldBe(expected);

    // -------------------------------------------------------------------------
    // Numbers with formatting
    // Separators (commas, periods, currency symbols) are dropped
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("1,000,000", "1000000")]
    [InlineData("3.14", "314")]
    [InlineData("£100", "100")]
    [InlineData("$99.99", "9999")]
    [InlineData("100%", "100")]
    [InlineData("#42", "42")]
    [InlineData("(1842)", "1842")]
    [InlineData("c.1750", "c1750")]
    [InlineData("pp.23-45", "pp2345")]
    public void Normalise_Numbers_FormattingDropped(string input, string expected)
        => Text.Normalise(input).ShouldBe(expected);

    // -------------------------------------------------------------------------
    // Brackets and quotation marks — all dropped, inner word preserved
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("(hello)", "hello")]
    [InlineData("[world]", "world")]
    [InlineData("{test}", "test")]
    [InlineData("\"quoted\"", "quoted")]               // ASCII double quote
    [InlineData("\u201Cquoted\u201D", "quoted")]       // curly double quotes
    [InlineData("\u2018quoted\u2019", "quoted")]       // curly single quotes
    [InlineData("(see p.42)", "see p42")]
    public void Normalise_BracketsAndQuotes_Dropped(string input, string expected)
        => Text.Normalise(input).ShouldBe(expected);

    // -------------------------------------------------------------------------
    // Ellipsis and multiple punctuation marks
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("...", "")]
    [InlineData("\u2026", "")]                     // Unicode ellipsis character
    [InlineData("hello...", "hello")]
    [InlineData("...hello", "hello")]
    [InlineData("hello...world", "helloworld")]    // no space inserted between parts
    [InlineData("hello. World", "hello world")]   // period dropped; space preserved
    [InlineData("---", "")]
    [InlineData("!!!test!!!", "test")]
    public void Normalise_Ellipsis_AndMultiplePunctuation(string input, string expected)
        => Text.Normalise(input).ShouldBe(expected);

    // -------------------------------------------------------------------------
    // Whitespace variants
    // All whitespace is collapsed to a single space; non-whitespace symbols are dropped
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("hello\u00A0world", "hello world")]  // non-breaking space
    [InlineData("hello\u2009world", "hello world")]  // thin space
    [InlineData("hello\r\nworld", "hello world")]    // Windows line ending
    [InlineData("hello\u000Bworld", "hello world")]  // vertical tab
    [InlineData("a  b  c", "a b c")]                 // multiple spaces
    [InlineData("  leading and trailing  ", "leading and trailing")]
    public void Normalise_WhitespaceVariants_CollapsedToSingleSpace(string input, string expected)
        => Text.Normalise(input).ShouldBe(expected);

    // -------------------------------------------------------------------------
    // Accented and non-ASCII letters — PRESERVED by the reference implementation
    //
    // IMPORTANT KNOWN LIMITATION: accented letters are kept as-is (lowercased).
    // "café" normalises to "caf\u00e9", NOT "cafe". This means a user searching
    // "cafe" (no accent) will NOT find "café" in the text. This replicates the
    // behaviour of both reference implementations exactly.
    // Do not change this behaviour silently.
    // -------------------------------------------------------------------------

    [Fact]
    public void Normalise_AccentedLetters_Preserved_KnownLimitation()
    {
        // Accented letters ARE letters (char.IsLetterOrDigit returns true)
        // so they are kept, just lowercased.
        Text.Normalise("café").ShouldBe("caf\u00e9");
        Text.Normalise("naïve").ShouldBe("na\u00efve");
        Text.Normalise("résumé").ShouldBe("r\u00e9sum\u00e9");
        Text.Normalise("über").ShouldBe("\u00fcber");

        // Consequence: searching the unaccented form will NOT find the accented form
        Text.Normalise("cafe").ShouldNotBe(Text.Normalise("café"));
    }

    // -------------------------------------------------------------------------
    // Strings that normalise to empty
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("---")]
    [InlineData("...")]
    [InlineData("£$%")]
    [InlineData("()[]{}")]
    [InlineData("\u2014\u2013")]   // em and en dash only
    public void Normalise_AllNonAlphanumeric_ProducesEmpty(string input)
        => Text.Normalise(input).ShouldBeEmpty();

    // -------------------------------------------------------------------------
    // Full sentence / paragraph — realistic OCR content
    // -------------------------------------------------------------------------

    [Fact]
    public void Normalise_FullSentence_RealisticContent()
    {
        var input = "Mr. Smith's report (1842) showed 1,250 people — or 12.5% — were affected.";
        var expected = "mr smiths report 1842 showed 1250 people or 125 were affected";
        Text.Normalise(input).ShouldBe(expected);
    }

    [Fact]
    public void Normalise_BibliographicEntry()
    {
        var input = "Smith, J. (ed.), \"The History of England,\" vol. 3, pp. 42–56, London, 1850.";
        var expected = "smith j ed the history of england vol 3 pp 4256 london 1850";
        Text.Normalise(input).ShouldBe(expected);
    }

    [Fact]
    public void Normalise_IsIdempotent_OnComplexInput()
    {
        var input = "Mr. O'Brien's COVID-19 study (2020) — a state-of-the-art analysis.";
        var once = Text.Normalise(input);
        Text.Normalise(once).ShouldBe(once);
    }

    // -------------------------------------------------------------------------
    // The symmetry property: text and query go through the same normalisation,
    // so punctuation differences between stored text and search query cancel out.
    // This is the core "maximise hit chance" property.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("don't", "dont")]       // stored as "don't", query as "dont"
    [InlineData("dont", "dont")]        // stored as "dont", query as "dont"
    [InlineData("COVID-19", "covid19")] // stored as "COVID-19", query as "covid19"
    [InlineData("COVID19", "covid19")]  // stored as "COVID19", query as "covid19"
    [InlineData("Mr.", "mr")]           // stored as "Mr.", query as "mr"
    [InlineData("mr", "mr")]            // stored as "mr", query as "mr"
    public void Normalise_QueryAndTextSymmetry(string textForm, string queryForm)
    {
        // Both the stored word and a possible query normalise to the same string,
        // guaranteeing IndexOf will find the match regardless of punctuation used.
        Text.Normalise(textForm).ShouldBe(Text.Normalise(queryForm));
    }
}
