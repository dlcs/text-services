# Memory Cache Usage — Design Notes

> **Status:** Recommendations 1 and 2 below are implemented in `TextServices.Search.Api`
> (`Program.cs` registers the cache with a `SizeLimit`; `Services/TextCache.cs` sets
> per-entry size and absolute expiration). Recommendation 3 is deployment guidance,
> captured in the `CacheMaxEntries` doc comment in `Configuration/SearchApiOptions.cs`.
> Recommendation 4 is deliberately not implemented — it remains a last resort.

## Original implementation issues (since addressed)

The `IMemoryCache` registration originally had no size limit at all. With large objects, this meant:

1. **The cache can grow unboundedly.** If a hundred different book IDs are requested, all 100 Text objects stay in memory until the sliding window expires. There's no back-pressure mechanism.

2. **Large Object Heap pressure.** The `NormalisedFullText` string for 100k words is roughly 700KB–1MB (lowercase, space-separated). The `Words` dictionary backing store will also be large. Anything over 85KB goes to the LOH, which is only collected in Gen2 and — critically — is **not compacted by default**. Cache evictions followed by re-loads fragment the LOH over time, leading to the classic "memory never comes back down" shape on a memory graph.

3. **No size accounting.** `IMemoryCache` supports `SizeLimit` + per-entry `Size`, but only if you opt in. Without it, the eviction policy (sliding expiry) is the *only* constraint.

## Memory estimates

For a 100k-word Text:
- Each `Word` carries ~8 integer fields + position data ≈ 200–300 bytes in .NET memory
- Word dictionary: ~25–35MB
- NormalisedFullText + RawFullText strings: ~1–2MB
- **Total: roughly 25–40MB per large Text**

Ten large texts cached simultaneously = 250–400MB just for the text objects, before ASP.NET's own working set.

## AWS ECS considerations

Yes, memory dominates over CPU here. CPU load is light — search is essentially `IndexOf` on an in-memory string. The ECS-specific issues are:

**Hard memory limits kill containers.** ECS tasks have a hard memory limit in the task definition. If the cache grows to fill it, the container gets OOMKilled and restarted — which flushes the cache entirely, causing a thundering herd on the next set of requests. This is a bad failure mode.

**No cache sharing between tasks.** Each ECS task has its own in-process cache. If you scale to 3 tasks under load, each independently warms its own cache. The *best* case is that each task caches only the texts relevant to its slice of traffic; the worst case is all 3 tasks cache the same popular texts, tripling memory consumption with no benefit. You either need sticky sessions at the load balancer (ALB target group stickiness) to route requests for the same book ID to the same task, or you accept the duplication.

**Cold starts matter.** A new ECS task starts with an empty cache. During scale-out events, S3 load spikes until the new task warms up. This is usually fine for S3 same-region (fast, cheap), but worth being aware of.

**CloudWatch memory metrics.** ECS doesn't expose container memory usage as a CloudWatch metric by default — you need the Container Insights agent. Without it you're flying blind on cache size.

## Recommended changes and their status

1. **Set a cache entry limit.** ✅ **Implemented.** Use entry count (not word count) as the size unit. Sizing by word count looks proportional but has a fatal flaw: a text larger than `SizeLimit` words can never be admitted to the cache at all, silently degrading to uncached storage reads for every request. Entry count avoids this — every text is cacheable regardless of size, and memory headroom is managed at the infrastructure level (ECS task memory limit).

```csharp
builder.Services.AddMemoryCache(opts => opts.SizeLimit = options.CacheMaxEntries); // e.g. 20

// When caching — each entry costs 1 slot regardless of size:
var entryOptions = new MemoryCacheEntryOptions()
    .SetSlidingExpiration(TimeSpan.FromMinutes(options.CacheSlidingExpirationMinutes))
    .SetSize(1);
```

   In the code: `Program.cs` registers the cache with `SizeLimit` taken from the `TextServices:CacheMaxEntries` setting (default 20; read directly from configuration because options binding isn't available at that point in startup). The config key is looked up via `nameof(SearchApiOptions.CacheMaxEntries)` rather than a string literal, so the `SearchApiOptions` property — otherwise unreferenced in code, since this path bypasses options binding — stays in lockstep with the setting it documents. `TextCache` sets `.SetSize(1)` on every entry — both `Text` and `AutoComplete` objects share the same slot budget.

2. **Add an absolute expiration floor.** ✅ **Implemented.** Sliding expiration alone means a popular text stays cached forever. Adding an absolute cap forces periodic refresh and bounds LOH lifetime. `TextCache` applies `.SetAbsoluteExpiration()` from the `TextServices:CacheAbsoluteExpirationHours` setting (default 4 hours) alongside the sliding expiration.

3. **ECS task sizing.** ☁️ **Deployment guidance, not code.** For a service caching up to ~5M words: budget ~200MB for the cache + ~150MB for ASP.NET baseline = size tasks at 512MB–1GB with the hard limit set higher than the soft limit so ECS scales out before OOMKilling. The per-text memory estimate (~30–40MB for a large text) is recorded in the `CacheMaxEntries` doc comment in `SearchApiOptions` for whoever sizes the task definition.

4. **Consider LOH compaction on a schedule** if you see fragmentation in production:
```csharp
GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
GC.Collect(2, GCCollectionMode.Forced, blocking: true);
```
But that's a last resort — the size limit approach is cleaner. ⏸️ **Deliberately not implemented**; revisit only if production memory graphs show LOH fragmentation.

The architecture is sound for the stated usage (few concurrent users, large objects). With the entry limit and absolute expiration now in place, the remaining risk sits at the infrastructure level: raise `CacheMaxEntries` only in step with the ECS task memory budget.
