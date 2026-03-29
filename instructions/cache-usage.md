# Memory Cache Usage — Design Notes

## Current implementation issues

The `IMemoryCache` registration has no size limit at all. With large objects, this means:

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

## What I'd recommend changing

1. **Set a cache size limit.** Use word count (or protobuf byte count from `ITextStore`) as the size unit:

```csharp
builder.Services.AddMemoryCache(opts => opts.SizeLimit = 5_000_000); // 5M words total

// When caching:
var entryOptions = new MemoryCacheEntryOptions()
    .SetSlidingExpiration(TimeSpan.FromMinutes(options.CacheSlidingExpirationMinutes))
    .SetSize(text.Words.Count); // evict large texts first
```

2. **Add an absolute expiration floor.** Sliding expiration alone means a popular text stays cached forever. Adding an absolute cap (e.g. 4 hours) forces periodic refresh and bounds LOH lifetime.

3. **ECS task sizing.** For a service caching up to ~5M words: budget ~200MB for the cache + ~150MB for ASP.NET baseline = size tasks at 512MB–1GB with the hard limit set higher than the soft limit so ECS scales out before OOMKilling.

4. **Consider LOH compaction on a schedule** if you see fragmentation in production:
```csharp
GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
GC.Collect(2, GCCollectionMode.Forced, blocking: true);
```
But that's a last resort — the size limit approach is cleaner.

The architecture is sound for the stated usage (few concurrent users, large objects). The risk is when "few" becomes "more" without the cache size guardrails in place.
