using AsyncKeyedLock;
using Microsoft.Extensions.Caching.Memory;
using TextServices.Core.Models;
using TextServices.Search.Api.Configuration;
using TextServices.Storage;

namespace TextServices.Search.Api.Services;

/// <summary>
/// <see cref="ITextCache"/> implementation using <see cref="IMemoryCache"/> with sliding
/// expiration, and <see cref="AsyncKeyedLocker{TKey}"/> to prevent thundering-herd on
/// simultaneous cache misses for the same key.
/// </summary>
public class TextCache(
    ITextStore textStore,
    IMemoryCache memoryCache,
    AsyncKeyedLocker<string> locker,
    SearchApiOptions options) : ITextCache
{
    public async Task<Text?> GetTextAsync(string key, CancellationToken ct = default)
        => await GetOrLoadAsync<Text>(
            cacheKey:  $"text:{key}",
            loadAsync: () => textStore.LoadText(key),
            ct);

    public async Task<AutoComplete?> GetAutoCompleteAsync(string key, CancellationToken ct = default)
        => await GetOrLoadAsync<AutoComplete>(
            cacheKey:  $"ac:{key}",
            loadAsync: () => textStore.LoadAutoComplete(key),
            ct);

    private async Task<T?> GetOrLoadAsync<T>(
        string cacheKey,
        Func<Task<T?>> loadAsync,
        CancellationToken ct) where T : class
    {
        // Fast path — already cached.
        if (memoryCache.TryGetValue(cacheKey, out T? cached))
            return cached;

        // Slow path — acquire a per-key lock so only one concurrent caller loads from storage.
        using (await locker.LockAsync(cacheKey, ct, continueOnCapturedContext: false))
        {
            // Re-check after acquiring the lock: another caller may have populated the cache
            // while this one was waiting.
            if (memoryCache.TryGetValue(cacheKey, out cached))
                return cached;

            var value = await loadAsync();

            if (value != null)
            {
                var expiry = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(
                        TimeSpan.FromMinutes(options.CacheSlidingExpirationMinutes));

                memoryCache.Set(cacheKey, value, expiry);
            }

            return value;
        }
    }
}
