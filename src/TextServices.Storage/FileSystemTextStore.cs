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
public class FileSystemTextStore : ITextStore
{
    private const string TextFileName         = "text.bin";
    private const string AutoCompleteFileName = "autocomplete.bin";
    private const string ManifestFileName     = "manifest.json";
    private const string RawTextFileName      = "rawtext.txt";
    private const string PdfFileName          = "book.pdf";
    private const string FiguresFileName      = "figures.json";
    private const string AnnotationsFileName  = "annotations.json";
    private const string CapabilitiesFileName  = "capabilities.json";
    private const string PageSequenceFileName  = "pagesequence.json";

    private readonly string _rootPath;

    public FileSystemTextStore(FileSystemTextStoreOptions options)
    {
        _rootPath = options.RootPath;
    }

    /// <inheritdoc/>
    public async Task SaveText(string key, Text text)
    {
        var path = GetPath(key, TextFileName);
        EnsureDirectory(path);
        await using var stream = File.Create(path);
        Serializer.Serialize(stream, text);
    }

    /// <inheritdoc/>
    public Task<Text?> LoadText(string key)
    {
        var path = GetPath(key, TextFileName);
        if (!File.Exists(path)) return Task.FromResult<Text?>(null);

        using var stream = File.OpenRead(path);
        var text = Serializer.Deserialize<Text>(stream);
        return Task.FromResult<Text?>(text);
    }

    /// <inheritdoc/>
    public async Task SaveAutoComplete(string key, AutoComplete autoComplete)
    {
        var path = GetPath(key, AutoCompleteFileName);
        EnsureDirectory(path);
        await using var stream = File.Create(path);
        Serializer.Serialize(stream, autoComplete);
    }

    /// <inheritdoc/>
    public Task<AutoComplete?> LoadAutoComplete(string key)
    {
        var path = GetPath(key, AutoCompleteFileName);
        if (!File.Exists(path)) return Task.FromResult<AutoComplete?>(null);

        using var stream = File.OpenRead(path);
        var ac = Serializer.Deserialize<AutoComplete>(stream);
        return Task.FromResult<AutoComplete?>(ac);
    }

    /// <inheritdoc/>
    public async Task SaveManifest(string key, string json)
    {
        var path = GetPath(key, ManifestFileName);
        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, json);
    }

    /// <inheritdoc/>
    public async Task<string?> LoadManifest(string key)
    {
        var path = GetPath(key, ManifestFileName);
        if (!File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path);
    }

    /// <inheritdoc/>
    public async Task SaveRawText(string key, string rawText)
    {
        var path = GetPath(key, RawTextFileName);
        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, rawText);
    }

    /// <inheritdoc/>
    public async Task<string?> LoadRawText(string key)
    {
        var path = GetPath(key, RawTextFileName);
        if (!File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path);
    }

    /// <inheritdoc/>
    public async Task SavePdf(string key, Stream pdfStream)
    {
        var path = GetPath(key, PdfFileName);
        EnsureDirectory(path);
        await using var file = File.Create(path);
        await pdfStream.CopyToAsync(file);
    }

    /// <inheritdoc/>
    public Task<Stream?> LoadPdf(string key)
    {
        var path = GetPath(key, PdfFileName);
        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(File.OpenRead(path));
    }

    /// <inheritdoc/>
    public async Task SaveFigures(string key, string json)
    {
        var path = GetPath(key, FiguresFileName);
        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, json);
    }

    /// <inheritdoc/>
    public async Task<string?> LoadFigures(string key)
    {
        var path = GetPath(key, FiguresFileName);
        if (!File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path);
    }

    /// <inheritdoc/>
    public async Task SaveAnnotations(string key, string json)
    {
        var path = GetPath(key, AnnotationsFileName);
        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, json);
    }

    /// <inheritdoc/>
    public async Task<string?> LoadAnnotations(string key)
    {
        var path = GetPath(key, AnnotationsFileName);
        if (!File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path);
    }

    /// <inheritdoc/>
    public Task<bool> Exists(string key)
    {
        var path = GetPath(key, TextFileName);
        return Task.FromResult(File.Exists(path));
    }

    /// <inheritdoc/>
    public async Task SaveCapabilities(string key, int services)
    {
        var path = GetPath(key, CapabilitiesFileName);
        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, services.ToString());
    }

    /// <inheritdoc/>
    public async Task<int?> LoadCapabilities(string key)
    {
        var path = GetPath(key, CapabilitiesFileName);
        if (!File.Exists(path)) return null;
        var text = await File.ReadAllTextAsync(path);
        return int.TryParse(text.Trim(), out var value) ? value : null;
    }

    /// <inheritdoc/>
    public async Task SavePageSequence(string key, string json)
    {
        var path = GetPath(key, PageSequenceFileName);
        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, json);
    }

    /// <inheritdoc/>
    public Task<string?> LoadPageSequence(string key)
    {
        var path = GetPath(key, PageSequenceFileName);
        if (!File.Exists(path)) return Task.FromResult<string?>(null);
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
