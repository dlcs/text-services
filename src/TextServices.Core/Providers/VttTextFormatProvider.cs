using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TextServices.Core.Models;

namespace TextServices.Core.Providers;

/// <summary>
/// Processes WebVTT caption/subtitle files into the text index.
/// Each VTT cue becomes a set of words sharing the same time range and line number.
/// </summary>
public class VttTextFormatProvider(ILogger<VttTextFormatProvider>? logger = null) : IStringFormatProvider
{
    private readonly ILogger<VttTextFormatProvider> _logger = logger ?? NullLogger<VttTextFormatProvider>.Instance;

    public bool Supports(string? profile, string? format, string? label) =>
        ContainsIgnoreCase(profile, "text/vtt") || ContainsIgnoreCase(format, "text/vtt") ||
        ContainsIgnoreCase(profile, "vtt") || ContainsIgnoreCase(format, "vtt") ||
        ContainsIgnoreCase(label, "vtt") || ContainsIgnoreCase(label, "webvtt") ||
        ContainsIgnoreCase(label, "transcript");

    public void ProcessPage(
        TextAccumulator accumulator,
        string rawContent,
        string imageIdentifier,
        int canvasWidth,
        int canvasHeight)
    {
        accumulator.BeginPage(imageIdentifier, isTemporalContent: true);

        // Normalise line endings and strip BOM
        var content = rawContent.TrimStart('\uFEFF').ReplaceLineEndings("\n");
        var lines = content.Split('\n');

        var i = 0;

        // Skip WEBVTT header and any NOTE/STYLE blocks before first cue
        while (i < lines.Length && !lines[i].Contains("-->"))
            i++;

        while (i < lines.Length)
        {
            // Skip blank lines between cues
            if (string.IsNullOrWhiteSpace(lines[i])) { i++; continue; }

            // Skip optional cue identifier (non-blank line that doesn't contain "-->")
            if (!lines[i].Contains("-->")) { i++; continue; }

            // Timing line
            var timingLine = lines[i++];
            int startMs = 0, endMs = 0;
            try
            {
                (startMs, endMs) = ParseTimingLine(timingLine);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to parse VTT timing line on page '{Id}': {Value}", imageIdentifier, timingLine);
            }

            // Collect cue text lines until blank line
            var cueLines = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
                cueLines.Add(lines[i++]);

            if (cueLines.Count == 0) continue;

            // Concatenate, strip markup tags, split into words
            var cueText = string.Join(" ", cueLines);
            var plainText = StripTags(cueText);

            accumulator.NextLine();

            foreach (var token in plainText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var raw = token;
                var norm = Text.Normalise(token);
                if (!string.IsNullOrEmpty(norm))
                    accumulator.AddWord(raw, norm, startMs, endMs);
            }
        }
    }

    private static (int startMs, int endMs) ParseTimingLine(string line)
    {
        // Format: "HH:MM:SS.mmm --> HH:MM:SS.mmm [settings]"
        var parts = line.Split("-->", 2, StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return (0, 0);

        // End time may have trailing cue settings (e.g. "00:00:10.167 align:start")
        var endToken = parts[1].Split(' ', 2)[0];

        return (ParseTimestamp(parts[0].Trim()), ParseTimestamp(endToken.Trim()));
    }

    private static int ParseTimestamp(string s)
    {
        // Accepts HH:MM:SS.mmm or MM:SS.mmm
        var dotIdx = s.IndexOf('.');
        var ms = dotIdx >= 0 ? int.Parse(s[(dotIdx + 1)..].PadRight(3, '0')[..3]) : 0;
        var timePart = dotIdx >= 0 ? s[..dotIdx] : s;
        var colonParts = timePart.Split(':');
        return colonParts.Length switch
        {
            3 => int.Parse(colonParts[0]) * 3_600_000
               + int.Parse(colonParts[1]) * 60_000
               + int.Parse(colonParts[2]) * 1_000
               + ms,
            2 => int.Parse(colonParts[0]) * 60_000
               + int.Parse(colonParts[1]) * 1_000
               + ms,
            _ => 0,
        };
    }

    /// <summary>Strips VTT inline markup tags (speaker tags, timestamp tags, class tags).</summary>
    private static string StripTags(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var inTag = false;
        foreach (var c in text)
        {
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool ContainsIgnoreCase(string? value, string term) =>
        value != null && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
