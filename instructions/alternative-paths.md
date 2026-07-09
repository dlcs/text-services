## Path Rewrites

By paths generated for the search api are:

* `/search/v2/{**id}?q={term}`
* `/search/v1/{**id}?q={term}`
* `/autocomplete/v2/{**id}?q={term}`
* `/autocomplete/v1/{**id}?q={term}`
* `/annotations/lines/v1/{n}/{**id}`
* `/annotations/words/v1/{n}/{**id}`
* `/text-augmented/v3/{**id}`
* `/proxy/image?uri={uri}`
* `/text/v1/{**id}`
* `/pdf/v1/{**id}`
* `/identified/figures/{**id}`

These are rendered onto generated Manifest using `{protocol}://{host}/{above-path}`

### Canonical Paths 

This is the "as is" processing.

Currently we have a `SearchBaseUrl` (say `https://search.default`). When generating a Manifest this is used to construct every `id`, so the above list of paths are appended to `SearchBaseUrl`.

The important thing is the `{**id}` is _always_ the job-id, it can contain any number of slashes and is replaced in it's entirety.

### Requirement

We need to be able to have some degree of control over what paths are rendered when returning. To do so we will support X-Forwarded-Host and X-Forwarded-Path

We need to be a way to be able to translate incoming requests, so that they reflect in the outgoing request - without adding any sort of rule-based _stuff_.

### Solution 

One solution to this is `X-Forwarded-Proto`, `X-Forwarded-Host` (standard HTTP headers) and `X-Forwarded-Path` (non-standard). These will all be added by proxy (e.g. CloudFront), if there are rewrite rules in place.

* `X-Forwarded-Proto` - configured via standard middleware. Ensure that the HttpContext has appropriate protocol.
* `X-Forwarded-Host` - will be used for the host if it is part of known whitelist. Added by proxy.
* `X-Forwarded-Path` - will be used if it is accompanied by a whitelisted `X-Forwarded-Host`. Added by proxy.
  * This isn't perfect adds an degree of safety. You can only rewrite path + host if we expect the host.
  * If we want to rewrite a path for canonical host it would need to be whitelisted, which feels like a safe trade-off

### Examples

Below examples work through requirements. Assume we're requesting the text-augmented adjunct, this looks at resulting value for `/autocomplete/v1` path. For all of these examples:
* Canonical hostname is `search.default`
* JobId is `2/cc/123`
* The actual http request that hits the search API is `https://search.default/text-augmented/v3/2/cc/123`

| Incoming (maybe via proxy)                        | X-Forwarded-Host | X-Forwarded-Path           | Autocomplete `id`                               | Notes                                                                                                          |
| ------------------------------------------------- | ---------------- | -------------------------- | ----------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| https://search.default/text-augmented/v3/2/cc/123 |                  |                            | https://search.default/autocomplete/v1/2/cc/123 | Default, no proxy                                                                                              |
| https://unknown.host/text-augmented/v3/2/cc/123   | unknown.host     |                            | https://search.default/autocomplete/v1/2/cc/123 | x-forwarded-host but unknown                                                                                   |
| https://unknown.host/text-augmented/v3/2/cc/123   |                  | text-augmented/v3/2/cc/123 | https://search.default/autocomplete/v1/2/cc/123 | x-forwarded-path but no accompanying x-forwarded-host                                                          |
| https://unknown.host/text-augmented/v3/2/cc/123   | unknown.host     | text-augmented/v3/2/cc/123 | https://search.default/autocomplete/v1/2/cc/123 | x-forwarded-path but accompanying x-forwarded-host is unknown                                                  |
| https://known.host/text-augmented/v3/2/cc/123     | known.host       |                            | https://known.host/autocomplete/v1/2/cc/123     | x-forwarded-host is whitelisted                                                                                |
| https://known.host/text-augmented/v3/cc/123       | known.host       | text-augmented/v3/cc/123   | https://known.host/autocomplete/v1/cc/123       | x-forwarded-host is whitelisted and x-forwarded-path is set (crucially it is NOT the `id`)                     |
| https://known.host/text-augmented/v3/cc/123       | known.host       |                            | https://known.host/autocomplete/v1/2/cc/123     | x-forwarded-host is whitelisted. x-forwarded-path not set so `id` is used. This would be a misconfigured proxy |

> [!NOTE]
> Some points to now from above:
> * The above outlines how `id` path is constructed for autocomplete path on generated Manifest but the same process would apply for any generated `id`
> * The `X-Forwarded-Path` may contain a query parameter (e.g. for search results), this should be removed from ids.

#### Implementation

The rough implementation would be to use the `X-Forwarded-Path` to determine the `{**id}` element to use in generated paths.

To do so (assuming `X-Forwarded-Path` is provided and valid) we will remove the current root (minus `{**id}`) from the start of the `X-Forwarded-Path`, this will yield the usable `id` for path generation.

### Implementation

All forwarded-header logic is centralised in `EndpointHelpers.Resolve` (`Features/EndpointHelpers.cs`), which reads `X-Forwarded-Host` and `X-Forwarded-Path` once and returns a `ResolvedRequest` record:

```csharp
internal record ResolvedRequest(string EffectiveId, string SelfUrl, string BaseUrl);
```

* `EffectiveId` — the job id to use in generated URLs (extracted from `X-Forwarded-Path` when the host is whitelisted; otherwise the original route id).
* `SelfUrl` — absolute URL for the current endpoint, already incorporating the effective id and optional query term.
* `BaseUrl` — scheme + authority only; used by `TextAugmentedEndpoints` as the base for all cross-endpoint service URLs.

Every endpoint calls `Resolve` once:

```csharp
var resolved = EndpointHelpers.Resolve(options.Value, ctx, "search/v1/", id, q);
// resolved.SelfUrl  → passed to the handler as the response @id
// resolved.BaseUrl  → used by TextAugmented to build service descriptor URLs
// resolved.EffectiveId → passed to TextAugmentedRequest as UrlId (see below)
```

`X-Forwarded-Proto` is handled separately by `ForwardedHeadersMiddleware` (configured in `ServiceCollectionExtensions.ConfigureForwardedHeaders`), which sets `Request.Scheme`. Trusted sources are restricted via `KnownNetworks` / `KnownProxies` config keys.

#### TextAugmented specifics

`TextAugmentedHandler` builds cross-endpoint URLs using both a storage id (to load artefacts) and a URL id (to generate service descriptors). These differ when `X-Forwarded-Path` rewrites the id. `TextAugmentedRequest` carries both:

```csharp
record TextAugmentedRequest(string Id, string SelfUrl, string SearchBaseUrl, string? UrlId = null)
```

The handler uses `UrlId ?? Id` for URL generation and `Id` for all storage lookups. The endpoint passes `resolved.EffectiveId` as `UrlId`.

#### Allowlist configuration

Permitted custom hosts are configured under `TextServices:AllowedCustomHosts` in `appsettings.json`. An empty array (the default) means both `X-Forwarded-Host` and `X-Forwarded-Path` are always ignored.