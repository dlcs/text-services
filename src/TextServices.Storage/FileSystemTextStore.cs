using Microsoft.Extensions.Logging;
using ProtoBuf;
using TextServices.Core.Models;

namespace TextServices.Storage;

/// <summary>
/// <see cref="ITextStore"/> implementation that stores artefacts as files on the
/// local filesystem under a configured root directory.
/// </summary>
/// <remarks>
/// Job key segments (split on <c>/</c>) become nested subdirectories, so the key
/// <c>"2/books/my-book"</c> is stored under <c>{RootPath}/2/books/my-book/</c>.
/// </remarks>
public class FileSystemTextStore(FileSystemTextStoreOptions options, ILogger<FileSystemTextStore> logger) : ITextStore
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

    private readonly string _rootPath = options.RootPath;

    /// <inheritdoc/>
    public async Task SaveText(string key, Text text)
    {
        var path = GetPath(key, TextFileName);
        EnsureDirectory(path);
        await using var stream = File.Create(path);
        Serializer.Serialize(stream, text);
        logger.LogDebug("Saved {Type} for '{Key}'", TextFileName, key);
    }

    /// <inheritdoc/>
    public Task<Text?> LoadText(string key)
    {
        var path = GetPath(key, TextFileName);
        if (!File.Exists(path))
        {
            logger.LogDebug("{Type} not found for '{Key}'", TextFileName, key);
            return Task.FromResult<Text?>(null);
        }

        using var stream = File.OpenRead(path);
        var text = Serializer.Deserialize<Text>(stream);
        logger.LogDebug("Loaded {Type} for '{Key}'", TextFileName, key);
        return Task.FromResult<Text?>(text);
    }

    /// <inheritdoc/>
    public async Task SaveAutoComplete(string key, AutoComplete autoComplete)
    {
        var path = GetPath(key, AutoCompleteFileName);
        EnsureDirectory(path);
        await using var stream = File.Create(path);
        Serializer.Serialize(stream, autoComplete);
        logger.LogDebug("Saved {Type} for '{Key}'", AutoCompleteFileName, key);
    }

    /// <inheritdoc/>
    public Task<AutoComplete?> LoadAutoComplete(string key)
    {
        var path = GetPath(key, AutoCompleteFileName);
        if (!File.Exists(path))
        {
            logger.LogDebug("{Type} not found for '{Key}'", AutoCompleteFileName, key);
            return Task.FromResult<AutoComplete?>(null);
        }

        using var stream = File.OpenRead(path);
        var ac = Serializer.Deserialize<AutoComplete>(stream);
        logger.LogDebug("Loaded {Type} for '{Key}'", AutoCompleteFileName, key);
        return Task.FromResult<AutoComplete?>(ac);
    }

    /// <inheritdoc/>
    public async Task SaveManifest(string key, string json)
    {
        var path = GetPath(key, ManifestFileName);
        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, json);
        logger.LogDebug("Saved {Type} for '{Key}'", ManifestFileName, key);
    }

    /// <inheritdoc/>
    public async Task<string?> LoadManifest(string key)
    {
        var path = GetPath(key, ManifestFileName);
        if (!File.Exists(path))
        {
            logger.LogDebug("{Type} not found for '{Key}'", ManifestFileName, key);
            return null;
        }
        var result = await File.ReadAllTextAsync(path);
        logger.LogDebug("Loaded {Type} for '{Key}'", ManifestFileName, key);
        return result;
    }

    /// <inheritdoc/>
    public async Task SaveRawText(string key, string rawText)
    {
        var path = GetPath(key, RawTextFileName);
        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, rawText);
        logger.LogDebug("Saved {Type} for '{Key}'", RawTextFileName, key);
    }

    /// <inheritdoc/>
    public async Task<string?> LoadRawText(string key)
    {
        var path = GetPath(key, RawTextFileName);
        if (!File.Exists(path))
        {
            logger.LogDebug("{Type} not found for '{Key}'", RawTextFileName, key);
            return null;
        }
        var result = await File.ReadAllTextAsync(path);
        logger.LogDebug("Loaded {Type} for '{Key}'", RawTextFileName, key);
        return result;
    }

    /// <inheritdoc/>
    public async Task SavePdf(string key, Stream pdfStream)
    {
        var path = GetPath(key, PdfFileName);
        EnsureDirectory(path);
        await using var file = File.Create(path);
        await pdfStream.CopyToAsync(file);
        logger.LogDebug("Saved {Type} for '{Key}'", PdfFileName, key);
    }

    /// <inheritdoc/>
    public Task<Stream?> LoadPdf(string key)
    {
        var path = GetPath(key, PdfFileName);
        if (!File.Exists(path))
        {
            logger.LogDebug("{Type} not found for '{Key}'", PdfFileName, key);
            return Task.FromResult<Stream?>(null);
        }
        logger.LogDebug("Loaded {Type} for '{Key}'", PdfFileName, key);
        return Task.FromResult<Stream?>(File.OpenRead(path));
    }

    /// <inheritdoc/>
    public async Task SaveFigures(string key, string json)
    {
        var path = GetPath(key, FiguresFileName);
        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, json);
        logger.LogDebug("Saved {Type} for '{Key}'", FiguresFileName, key);
    }

    /// <inheritdoc/>
    public async Task<string?> LoadFigures(string key)
    {
        var path = GetPath(key, FiguresFileName);
        if (!File.Exists(path))
        {
            logger.LogDebug("{Type} not found for '{Key}'", FiguresFileName, key);
            return null;
        }
        var result = await File.ReadAllTextAsync(path);
        logger.LogDebug("Loaded {Type} for '{Key}'", FiguresFileName, key);
        return result;
    }

    /// <inheritdoc/>
    public async Task SaveAnnotations(string key, string json)
    {
        var path = GetPath(key, AnnotationsFileName);
        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, json);
        logger.LogDebug("Saved {Type} for '{Key}'", AnnotationsFileName, key);
    }

    /// <inheritdoc/>
    public async Task<string?> LoadAnnotations(string key)
    {
        var path = GetPath(key, AnnotationsFileName);
        if (!File.Exists(path))
        {
            logger.LogDebug("{Type} not found for '{Key}'", AnnotationsFileName, key);
            return null;
        }
        var result = await File.ReadAllTextAsync(path);
        logger.LogDebug("Loaded {Type} for '{Key}'", AnnotationsFileName, key);
        return result;
    }

    /// <inheritdoc/>
    public Task<bool> Exists(string key)
    {
        var path = GetPath(key, TextFileName);
        var exists = File.Exists(path);
        logger.LogDebug("Artefact {Exists} for '{Key}'", exists, key);
        return Task.FromResult(exists);
    }

    /// <inheritdoc/>
    public async Task SaveCapabilities(string key, int services)
    {
        var path = GetPath(key, CapabilitiesFileName);
        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, services.ToString());
        logger.LogDebug("Saved {Type} for '{Key}'", CapabilitiesFileName, key);
    }

    /// <inheritdoc/>
    public async Task<int?> LoadCapabilities(string key)
    {
        var path = GetPath(key, CapabilitiesFileName);
        if (!File.Exists(path))
        {
            logger.LogDebug("{Type} not found for '{Key}'", CapabilitiesFileName, key);
            return null;
        }
        var text = await File.ReadAllTextAsync(path);
        logger.LogDebug("Loaded {Type} for '{Key}'", CapabilitiesFileName, key);
        return int.TryParse(text.Trim(), out var value) ? value : null;
    }

    /// <inheritdoc/>
    public async Task SavePageSequence(string key, string json)
    {
        var path = GetPath(key, PageSequenceFileName);
        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, json);
        logger.LogDebug("Saved {Type} for '{Key}'", PageSequenceFileName, key);
    }

    /// <inheritdoc/>
    public Task<string?> LoadPageSequence(string key)
    {
        var path = GetPath(key, PageSequenceFileName);
        if (!File.Exists(path))
        {
            logger.LogDebug("{Type} not found for '{Key}'", PageSequenceFileName, key);
            return Task.FromResult<string?>(null);
        }
        logger.LogDebug("Loaded {Type} for '{Key}'", PageSequenceFileName, key);
        return File.ReadAllTextAsync(path)!;
    }

    /// <inheritdoc/>
    public Task DeleteArtefacts(string key)
    {
        string[] fileNames =
        [
            TextFileName, AutoCompleteFileName, ManifestFileName, RawTextFileName,
            PdfFileName, FiguresFileName, AnnotationsFileName, CapabilitiesFileName,
            PageSequenceFileName,
        ];
        foreach (var fileName in fileNames)
        {
            var path = GetPath(key, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
        logger.LogDebug("Deleted artefacts for '{Key}'", key);
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private string GetPath(string key, string fileName)
    {
        // Split on '/' to turn e.g. "2/books/my-book" into nested subdirectories.
        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parts = new string[segments.Length + 2];
        parts[0] = _rootPath;
        segments.CopyTo(parts, 1);
        parts[^1] = fileName;
        return Path.Combine(parts);
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
