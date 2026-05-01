namespace TextServices.Builder.Api.Features.Jobs;

/// <summary>
/// Defines a generated page that displays a centred text message — used for redactions,
/// missing pages, and similar placeholder content.
/// Referenced by <see cref="PageInstruction.Type"/> via <see cref="JobInstruction.CustomTypes"/>.
/// </summary>
public class CustomPageType
{
    /// <summary>Text displayed centred on the generated PDF page.</summary>
    public string Message { get; set; } = string.Empty;
}
