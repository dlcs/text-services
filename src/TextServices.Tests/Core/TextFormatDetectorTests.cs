using Shouldly;
using TextServices.Core.Providers;

namespace TextServices.Tests.Core;

public class TextFormatDetectorTests
{
    [Theory]
    [InlineData("http://www.loc.gov/standards/alto/ns-v3#", null, null, TextFormat.Alto)]
    [InlineData("http://www.loc.gov/standards/alto/ns-v2#", null, null, TextFormat.Alto)]
    [InlineData("http://schema.ccs-gmbh.com/ALTO", null, null, TextFormat.Alto)]
    [InlineData(null, null, "METS-ALTO XML", TextFormat.Alto)]
    [InlineData("text/vnd.hocr+html", null, null, TextFormat.Hocr)]
    [InlineData("application/hocr+html", null, null, TextFormat.Hocr)]
    [InlineData("https://example.org/formats/hocr", null, null, TextFormat.Hocr)]
    [InlineData(null, null, "Tesseract hocr", TextFormat.Hocr)]
    [InlineData(null, W3cAnnotationTextFormatProvider.FormatSentinel, null, TextFormat.AnnotationPage)]
    [InlineData("application/json", null, null, TextFormat.Unknown)]
    [InlineData("http://iiif.io/api/presentation/3", null, null, TextFormat.Unknown)]
    [InlineData(null, "text/vtt", null, TextFormat.Vtt)]
    [InlineData(null, "application/alto+xml", null, TextFormat.Alto)]
    [InlineData(null, null, "METS-ALTO", TextFormat.Alto)]
    [InlineData(null, null, "Tesseract hOCR output", TextFormat.Hocr)]
    [InlineData(null, null, "WebVTT", TextFormat.Vtt)]
    [InlineData(null, null, "Transcript", TextFormat.Vtt)]
    [InlineData(null, null, "Audio transcription", TextFormat.Vtt)]
    [InlineData(null, null, "Tesseract hOCR transcription", TextFormat.Hocr)]
    [InlineData("https://example.org/transcript", null, null, TextFormat.Unknown)]
    [InlineData(null, null, null, TextFormat.Unknown)]
    [InlineData("http://www.tei-c.org/ns/1.0", null, "TEI", TextFormat.Unknown)]
    public void Detect_IdentifiesFormat(string? profile, string? format, string? label, TextFormat expected)
        => TextFormatDetector.Detect(profile, format, label).ShouldBe(expected);

    [Fact]
    public void Detect_ProfileTakesPrecedenceOverLabel()
        => TextFormatDetector.Detect("http://www.loc.gov/standards/alto/ns-v3#", null, "WebVTT")
            .ShouldBe(TextFormat.Alto);

    [Fact]
    public void Detect_FormatTakesPrecedenceOverLabel()
        => TextFormatDetector.Detect(null, "text/vtt", "METS-ALTO")
            .ShouldBe(TextFormat.Vtt);

    [Fact]
    public void Detect_UnrecognisedProfile_FallsBackToLabel()
        => TextFormatDetector.Detect("https://example.org/some-profile", null, "METS-ALTO")
            .ShouldBe(TextFormat.Alto);

    [Fact]
    public void Detect_MetsAltoTranscriptionLabel_IsAlto()
        => TextFormatDetector.Detect(null, null, "METS-ALTO transcription")
            .ShouldBe(TextFormat.Alto);
}
