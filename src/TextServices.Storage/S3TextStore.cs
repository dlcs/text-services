using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoBuf;
using TextServices.Core.Models;

namespace TextServices.Storage;

/// <summary>
/// <see cref="ITextStore"/> implementation that stores artefacts as objects in an Amazon S3 bucket.
/// </summary>
/// <remarks>
/// Job key segments (split on <c>/</c>) become S3 key path components, so the key
/// <c>"2/books/my-book"</c> is stored under <c>{KeyPrefix}2/books/my-book/</c>.
/// </remarks>
public class S3TextStore(IOptions<S3TextStoreOptions> options, IAmazonS3 s3, ILogger<S3TextStore> logger) : ITextStore, IDisposable
{
    private const string TextFileName = "text.bin";
    private const string AutoCompleteFileName = "autocomplete.bin";
    private const string ManifestFileName = "manifest.json";
    private const string RawTextFileName = "rawtext.txt";
    private const string PdfFileName = "book.pdf";
    private const string FiguresFileName = "figures.json";
    private const string AnnotationsFileName = "annotations.json";
    private const string CapabilitiesFileName = "capabilities.json";
    private const string PageSequenceFileName = "pagesequence.json";

    private readonly string _bucket = options.Value.BucketName;
    private readonly string _prefix = string.IsNullOrEmpty(options.Value.KeyPrefix)
        ? string.Empty
        : options.Value.KeyPrefix.TrimEnd('/') + '/';

    /// <inheritdoc/>
    public async Task SaveText(string key, Text text)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, text);
        await PutObjectAsync(GetS3Key(key, TextFileName), stream, "application/octet-stream");
        logger.LogDebug("Saved {Type} for '{Key}'", TextFileName, key);
    }

    /// <inheritdoc/>
    public async Task<Text?> LoadText(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, TextFileName), key);
        if (stream == null) return null;
        logger.LogDebug("Loaded {Type} for '{Key}'", TextFileName, key);
        using (stream) return Serializer.Deserialize<Text>(stream);
    }

    /// <inheritdoc/>
    public async Task SaveAutoComplete(string key, AutoComplete autoComplete)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, autoComplete);
        await PutObjectAsync(GetS3Key(key, AutoCompleteFileName), stream, "application/octet-stream");
        logger.LogDebug("Saved {Type} for '{Key}'", AutoCompleteFileName, key);
    }

    /// <inheritdoc/>
    public async Task<AutoComplete?> LoadAutoComplete(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, AutoCompleteFileName), key);
        if (stream == null) return null;
        logger.LogDebug("Loaded {Type} for '{Key}'", AutoCompleteFileName, key);
        using (stream) return Serializer.Deserialize<AutoComplete>(stream);
    }

    /// <inheritdoc/>
    public async Task SaveManifest(string key, string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await PutObjectAsync(GetS3Key(key, ManifestFileName), stream, "application/json");
        logger.LogDebug("Saved {Type} for '{Key}'", ManifestFileName, key);
    }

    /// <inheritdoc/>
    public async Task<string?> LoadManifest(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, ManifestFileName), key);
        if (stream == null) return null;
        logger.LogDebug("Loaded {Type} for '{Key}'", ManifestFileName, key);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <inheritdoc/>
    public async Task SaveRawText(string key, string rawText)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rawText));
        await PutObjectAsync(GetS3Key(key, RawTextFileName), stream, "text/plain");
        logger.LogDebug("Saved {Type} for '{Key}'", RawTextFileName, key);
    }

    /// <inheritdoc/>
    public async Task<string?> LoadRawText(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, RawTextFileName), key);
        if (stream == null) return null;
        logger.LogDebug("Loaded {Type} for '{Key}'", RawTextFileName, key);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <inheritdoc/>
    public async Task SavePdf(string key, Stream pdfStream)
    {
        using var ms = new MemoryStream();
        await pdfStream.CopyToAsync(ms);
        await PutObjectAsync(GetS3Key(key, PdfFileName), ms, "application/pdf");
        logger.LogDebug("Saved {Type} for '{Key}'", PdfFileName, key);
    }

    /// <inheritdoc/>
    public async Task<Stream?> LoadPdf(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, PdfFileName), key);
        if (stream != null) logger.LogDebug("Loaded {Type} for '{Key}'", PdfFileName, key);
        return stream;
    }

    /// <inheritdoc/>
    public async Task SaveFigures(string key, string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await PutObjectAsync(GetS3Key(key, FiguresFileName), stream, "application/json");
        logger.LogDebug("Saved {Type} for '{Key}'", FiguresFileName, key);
    }

    /// <inheritdoc/>
    public async Task<string?> LoadFigures(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, FiguresFileName), key);
        if (stream == null) return null;
        logger.LogDebug("Loaded {Type} for '{Key}'", FiguresFileName, key);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <inheritdoc/>
    public async Task SaveAnnotations(string key, string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await PutObjectAsync(GetS3Key(key, AnnotationsFileName), stream, "application/json");
        logger.LogDebug("Saved {Type} for '{Key}'", AnnotationsFileName, key);
    }

    /// <inheritdoc/>
    public async Task<string?> LoadAnnotations(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, AnnotationsFileName), key);
        if (stream == null) return null;
        logger.LogDebug("Loaded {Type} for '{Key}'", AnnotationsFileName, key);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <inheritdoc/>
    public async Task<bool> Exists(string key)
    {
        try
        {
            await s3.GetObjectMetadataAsync(_bucket, GetS3Key(key, TextFileName));
            logger.LogDebug("Artefact exists for '{Key}'", key);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogDebug("Artefact not found for '{Key}'", key);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task SaveCapabilities(string key, int services)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(services.ToString()));
        await PutObjectAsync(GetS3Key(key, CapabilitiesFileName), stream, "text/plain");
        logger.LogDebug("Saved {Type} for '{Key}'", CapabilitiesFileName, key);
    }

    /// <inheritdoc/>
    public async Task<int?> LoadCapabilities(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, CapabilitiesFileName), key);
        if (stream == null) return null;
        logger.LogDebug("Loaded {Type} for '{Key}'", CapabilitiesFileName, key);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        return int.TryParse(text.Trim(), out var value) ? value : null;
    }

    /// <inheritdoc/>
    public async Task SavePageSequence(string key, string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await PutObjectAsync(GetS3Key(key, PageSequenceFileName), stream, "application/json");
        logger.LogDebug("Saved {Type} for '{Key}'", PageSequenceFileName, key);
    }

    /// <inheritdoc/>
    public async Task<string?> LoadPageSequence(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, PageSequenceFileName), key);
        if (stream == null) return null;
        logger.LogDebug("Loaded {Type} for '{Key}'", PageSequenceFileName, key);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <inheritdoc/>
    public async Task DeleteArtefacts(string key)
    {
        string[] fileNames =
        [
            TextFileName, AutoCompleteFileName, ManifestFileName, RawTextFileName,
            PdfFileName, FiguresFileName, AnnotationsFileName, CapabilitiesFileName,
            PageSequenceFileName,
        ];
        foreach (var fileName in fileNames)
        {
            try
            {
                await s3.DeleteObjectAsync(_bucket, GetS3Key(key, fileName));
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Idempotent — object not found is not an error
            }
        }
        logger.LogDebug("Deleted artefacts for '{Key}'", key);
    }

    public void Dispose() => s3.Dispose();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private string GetS3Key(string key, string fileName)
        => $"{_prefix}{key}/{fileName}";

    private async Task PutObjectAsync(string s3Key, MemoryStream stream, string contentType)
    {
        stream.Position = 0;
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = s3Key,
            InputStream = stream,
            ContentType = contentType
        });
    }

    private async Task<Stream?> GetObjectStreamAsync(string s3Key, string key)
    {
        try
        {
            var response = await s3.GetObjectAsync(_bucket, s3Key);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogDebug("{S3Key} not found for '{Key}'", s3Key, key);
            return null;
        }
        catch (AmazonS3Exception ex)
        {
            logger.LogWarning(ex, "Unexpected S3 error loading '{S3Key}'", s3Key);
            throw;
        }
    }
}
