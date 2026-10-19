namespace TextServices.Core.Providers;

/// <summary>Text formats that can be identified from IIIF link metadata.</summary>
public enum TextFormat
{
    Unknown,
    Alto,
    Hocr,
    Vtt,
    AnnotationPage,
}

/// <summary>
/// Identifies the format of a linked text resource from its IIIF <c>profile</c>,
/// <c>format</c> and <c>label</c> metadata.
/// </summary>
/// <remarks>
/// <c>profile</c> and <c>format</c> are authoritative and are checked first.
/// <c>label</c> is free text supplied by the manifest author, so it is only consulted
/// when neither <c>profile</c> nor <c>format</c> identifies a format.
/// </remarks>
public static class TextFormatDetector
{
    public static TextFormat Detect(string? profile, string? format, string? label)
    {
        if (format == W3cAnnotationTextFormatProvider.FormatSentinel) return TextFormat.AnnotationPage;

        var detected = Match(profile);
        if (detected == TextFormat.Unknown) detected = Match(format);
        if (detected == TextFormat.Unknown) detected = Match(label, isLabel: true);
        return detected;
    }

    private static TextFormat Match(string? value, bool isLabel = false)
    {
        if (string.IsNullOrEmpty(value)) return TextFormat.Unknown;
        if (value.Contains("alto", StringComparison.OrdinalIgnoreCase)) return TextFormat.Alto;
        if (value.Contains("hocr", StringComparison.OrdinalIgnoreCase)) return TextFormat.Hocr;
        if (value.Contains("vtt", StringComparison.OrdinalIgnoreCase)) return TextFormat.Vtt;
        // A human-readable "transcript"/"transcription" label suggests VTT, but only once
        // the more specific formats above have been ruled out.
        if (isLabel && value.Contains("transcript", StringComparison.OrdinalIgnoreCase)) return TextFormat.Vtt;
        return TextFormat.Unknown;
    }
}
