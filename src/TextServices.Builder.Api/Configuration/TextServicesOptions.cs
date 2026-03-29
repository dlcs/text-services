namespace TextServices.Builder.Api.Configuration;

public class TextServicesOptions
{
    /// <summary>
    /// Base URL of the deployed Search API (e.g. "https://search.example.org").
    /// Used to compute the searchV1/autocompleteV1 URLs in the job status response.
    /// Leave empty if the Search API is not yet deployed.
    /// </summary>
    public string SearchApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Options for the filesystem text store.</summary>
    public StorageOptions Storage { get; set; } = new();
}

public class StorageOptions
{
    /// <summary>Root path under which text artefacts are stored on the filesystem.</summary>
    public string RootPath { get; set; } = "textservices-data";
}
