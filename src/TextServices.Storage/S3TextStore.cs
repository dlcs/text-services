using Amazon.S3;
using Amazon.S3.Model;
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
public class S3TextStore : ITextStore, IDisposable
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

    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly string _prefix;

    public S3TextStore(S3TextStoreOptions options, IAmazonS3 s3)
    {
        _s3 = s3;
        _bucket = options.BucketName;
        _prefix = string.IsNullOrEmpty(options.KeyPrefix)
            ? string.Empty
            : options.KeyPrefix.TrimEnd('/') + '/';
    }

    /// <inheritdoc/>
    public async Task SaveText(string key, Text text)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, text);
        await PutObjectAsync(GetS3Key(key, TextFileName), stream, "application/octet-stream");
    }

    /// <inheritdoc/>
    public async Task<Text?> LoadText(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, TextFileName));
        if (stream == null) return null;
        using (stream) return Serializer.Deserialize<Text>(stream);
    }

    /// <inheritdoc/>
    public async Task SaveAutoComplete(string key, AutoComplete autoComplete)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, autoComplete);
        await PutObjectAsync(GetS3Key(key, AutoCompleteFileName), stream, "application/octet-stream");
    }

    /// <inheritdoc/>
    public async Task<AutoComplete?> LoadAutoComplete(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, AutoCompleteFileName));
        if (stream == null) return null;
        using (stream) return Serializer.Deserialize<AutoComplete>(stream);
    }

    /// <inheritdoc/>
    public async Task SaveManifest(string key, string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await PutObjectAsync(GetS3Key(key, ManifestFileName), stream, "application/json");
    }

    /// <inheritdoc/>
    public async Task<string?> LoadManifest(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, ManifestFileName));
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <inheritdoc/>
    public async Task SaveRawText(string key, string rawText)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rawText));
        await PutObjectAsync(GetS3Key(key, RawTextFileName), stream, "text/plain");
    }

    /// <inheritdoc/>
    public async Task<string?> LoadRawText(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, RawTextFileName));
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <inheritdoc/>
    public async Task SavePdf(string key, Stream pdfStream)
    {
        using var ms = new MemoryStream();
        await pdfStream.CopyToAsync(ms);
        await PutObjectAsync(GetS3Key(key, PdfFileName), ms, "application/pdf");
    }

    /// <inheritdoc/>
    public async Task<Stream?> LoadPdf(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, PdfFileName));
        return stream;
    }

    /// <inheritdoc/>
    public async Task SaveFigures(string key, string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await PutObjectAsync(GetS3Key(key, FiguresFileName), stream, "application/json");
    }

    /// <inheritdoc/>
    public async Task<string?> LoadFigures(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, FiguresFileName));
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <inheritdoc/>
    public async Task SaveAnnotations(string key, string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await PutObjectAsync(GetS3Key(key, AnnotationsFileName), stream, "application/json");
    }

    /// <inheritdoc/>
    public async Task<string?> LoadAnnotations(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, AnnotationsFileName));
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <inheritdoc/>
    public async Task<bool> Exists(string key)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_bucket, GetS3Key(key, TextFileName));
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task SaveCapabilities(string key, int services)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(services.ToString()));
        await PutObjectAsync(GetS3Key(key, CapabilitiesFileName), stream, "text/plain");
    }

    /// <inheritdoc/>
    public async Task<int?> LoadCapabilities(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, CapabilitiesFileName));
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        return int.TryParse(text.Trim(), out var value) ? value : null;
    }

    /// <inheritdoc/>
    public async Task SavePageSequence(string key, string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await PutObjectAsync(GetS3Key(key, PageSequenceFileName), stream, "application/json");
    }

    /// <inheritdoc/>
    public async Task<string?> LoadPageSequence(string key)
    {
        var stream = await GetObjectStreamAsync(GetS3Key(key, PageSequenceFileName));
        if (stream == null) return null;
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
                await _s3.DeleteObjectAsync(_bucket, GetS3Key(key, fileName));
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Idempotent — object not found is not an error
            }
        }
    }

    public void Dispose() => _s3.Dispose();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private string GetS3Key(string key, string fileName)
        => $"{_prefix}{key}/{fileName}";

    private async Task PutObjectAsync(string s3Key, MemoryStream stream, string contentType)
    {
        stream.Position = 0;
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = s3Key,
            InputStream = stream,
            ContentType = contentType
        });
    }

    private async Task<Stream?> GetObjectStreamAsync(string s3Key)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_bucket, s3Key);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
