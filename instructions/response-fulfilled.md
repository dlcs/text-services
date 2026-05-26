# Tracking Requested + Fulfilled Properties

## Prompt

The JobServices bitmask controls which operations are done and which properties of the JobResponse are set. However, there is a possibility that all derivatives couldn't be generated.

Say in the event of the incoming Manifest not actually containing any text - this would prevent us generating FullText or Search resources. How can we flag this to the consumer? Also, how can we flag the work that was done vs the work that was asked? i.e. if you ask for the JobServices.Search but we couldn't fulfil that - should the Search service be added to the Manifest? Should the "search" property be returned on the JobResponse?

---

## Solution

Track **what was actually fulfilled** separately from what was requested, and use the fulfilled bitmask for all consumer-facing outputs.

### Summary of changes

- Add `FulfilledServices` (int?, nullable) to `BuilderJob` entity + EF migration.
- Compute the fulfilled bitmask at end of `TextBuildJob.ProcessPages()` based on what was actually saved. Return it from the method so `ExecuteAsync` can assign `job.FulfilledServices`. For the `catch` (Failed path) set it to `JobServices.None`.
- Always write `capabilities.json` using the fulfilled bitmask (not the requested one, and unconditionally rather than only when `services != All`). This ensures the Search API's `IsEnabledAsync()` correctly gates endpoints.
- Add `FulfilledServices` (`JobServices?`) to `JobResponse`. Use it (falling back to `Services` for pre-existing null rows) when deciding which endpoint URLs to populate.

No changes needed in `TextServices.Search.Api` — `IsEnabledAsync()` already gates correctly once `capabilities.json` contains the fulfilled value. The text-augmented manifest automatically omits service descriptors for unfulfilled services.

### Fulfilled bitmask logic

```csharp
var fulfilled = JobServices.None;
bool textSaved = !result.IsEmpty && (services.HasFlag(JobServices.Search) || services.HasFlag(JobServices.Pdf));

if (textSaved)
{
    if (services.HasFlag(JobServices.Search)) fulfilled |= JobServices.Search;
    if (services.HasFlag(JobServices.Pdf))    fulfilled |= JobServices.Pdf;
}
if (!result.IsEmpty && services.HasFlag(JobServices.Autocomplete))
    fulfilled |= JobServices.Autocomplete;
if (!result.IsEmpty && services.HasFlag(JobServices.FullText) && !string.IsNullOrEmpty(result.Text.RawFullText))
    fulfilled |= JobServices.FullText;
if (!result.IsEmpty && services.HasFlag(JobServices.Figures) && figuresJson != null)
    fulfilled |= JobServices.Figures;
if (!result.IsEmpty && services.HasFlag(JobServices.Annotations) && annotationsJson != null)
    fulfilled |= JobServices.Annotations;
if (services.HasFlag(JobServices.TextAugmented) && manifestSaved)
    fulfilled |= JobServices.TextAugmented;
```

### Files changed

| File | Change |
|---|---|
| `src/TextServices.Builder.Api/Data/BuilderJob.cs` | Add `FulfilledServices` (int?) |
| `src/TextServices.Builder.Api/Jobs/TextBuildJob.cs` | Compute fulfilled bitmask; always write capabilities with it |
| `src/TextServices.Builder.Api/Features/Jobs/JobResponse.cs` | Add `FulfilledServices` field; use it for endpoint URLs |
| `src/TextServices.Builder.Api/Migrations/` | New migration for `fulfilled_services` column |

### Verification

1. Job against a manifest with no text: `FulfilledServices = 0`, all endpoint URL fields null, capabilities.json = `0`, Search API returns 404.
2. Normal job: `FulfilledServices` matches `Services`, all URLs populated.
3. Pre-existing jobs (null `FulfilledServices`): endpoint URLs fall back to `Services` — no regression.
4. `dotnet test src/TextServices.sln`

