namespace TextServices.Storage;

/// <summary>
/// Bitmask of services/derivatives a job should produce and expose.
/// Stored as an <see cref="int"/> in the database and as a plain integer in
/// the <c>capabilities.json</c> artefact file read by the Search API.
/// </summary>
[Flags]
public enum JobServices
{
    None = 0,
    Search = 1 << 0,  // IIIF Search v1/v2 endpoints
    Autocomplete = 1 << 1,  // IIIF Search autocomplete endpoints
    FullText = 1 << 2,  // Raw plain-text endpoint
    Annotations = 1 << 3,  // Per-canvas and manifest line-annotation endpoints
    Pdf = 1 << 4,  // Searchable PDF endpoint (on-demand generation)
    TextAugmented = 1 << 5,  // Decorated manifest (text-augmented) endpoint
    Figures = 1 << 6,  // Identified figures (ComposedBlock) endpoint
    All = ~0,
}
