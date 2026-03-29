# TextServices — Design Decisions & Interaction Log

This file records key design decisions, questions, and responses made during development.
Code-level decisions are in CLAUDE.md. This log covers the *why*.

---

## 2026-03-29 — Initial design session

### Context
Tom Crane (Digirati) briefed Claude on the requirements via `instructions/text-services.md`.
Claude read both reference implementations in full:
- Wellcome Collection: `C:/git/wellcomecollection/iiif-builder/src/Wellcome.Dds/`
- St Louis Fed: `C:/git/digirati-co-uk/st-louis-fed/src/IIIFBuilder/`

### Questions asked and answers given

**Q1: Root namespace / solution name?**
> "TextServices"

**Q2: Job queue — simple in-process Channel+BackgroundService vs Hangfire vs other?**
> "I am not familiar with Hangfire, but am willing to try it out. I assume that it will work fine in AWS, Azure etc as well as local filesystem?"

*Confirmed: Hangfire uses a pluggable storage backend (PostgreSQL, Redis, SQL Server). Works identically with local PostgreSQL, AWS RDS, and Azure Database for PostgreSQL. Just change the connection string.*

**Q3: AutoComplete storage — embedded in Text object (Wellcome pattern) vs separate file (St Louis pattern)?**
> "Let's go for the St Louis separate file pattern - unless you recommend otherwise"

*No counter-recommendation — separate storage is the better design: the Search API can load AutoComplete independently from the (larger) Text object when serving autocomplete-only requests.*

**Q4: IIIF JSON building — use iiif-net NuGet package or plain JSON?**
> "I think keep to simple JSON for now. We might use iiif-net later but I don't think it's necessary right now."

**Q5: MediatR — use it or not?**
> "Use MediatR"

### Additional request
Tom asked for this interaction log to be kept in the repo:
> "is it possible for you to log all of our interactions on this project (not the code churning, just the main interactions and my responses to them) so we have a record"

*This file is the result. It will be updated at the end of each significant working session.*

### Agreed architecture summary
- Three logical components: Core class library, Builder API, Search API
- Five C# projects + two test projects (see CLAUDE.md for full structure)
- protobuf-net serialisation (proven in production in both reference implementations)
- Hangfire + PostgreSQL for durable job queue
- EntityFramework Core + Migrations for job state
- MediatR in both APIs
- Plain `System.Text.Json` for IIIF JSON (no iiif-net for now)
- `ITextFormatProvider` interface for format extensibility (ALTO first, hOCR etc. later)
- ALTO ns-v2 and ns-v3 both supported
- IIIF Search v1 first; v2 phased in later
- Builder API and Search API are separate ASP.NET applications

---

## 2026-03-29 — PR 1 decisions

### FluentAssertions → Shouldly

**Q: FluentAssertions 8.x showed a commercial licence warning in test output. What alternative do you recommend?**
> "yes please and update the PR"

Switched to **Shouldly 4.x** (MIT licence). Near-identical expressiveness; `.Should().Be(x)` becomes `.ShouldBe(x)` etc. One minor difference: nullable `string?` properties need a null-assertion step before calling string-specific methods like `ShouldContain`.

### Thorough comparison with reference implementations (prompted by PR review)

Tom asked for a direct line-by-line comparison of the `Text` implementation against both references.
Several differences were found and corrected:

**Search algorithm**: original used LINQ filtering over all words; reference walks *back* from the
IndexOf hit to the word boundary, then steps forward via `word.LenNorm + 1`. Substring matches
(e.g. "ick") correctly expand to the whole containing word ("quick"). `startPos` advances past
all matched words, not just `+1`.

**AddContext**: original used 6-word context on first/last rect of each hit group; reference uses
150 raw characters before/after, applied to **every** ResultRect individually, and skipped entirely
when there are ≥100 results.

**Coalescing bounding box**: reference uses `Math.Min(Y)` / `Math.Max(H)` independently (not
geometrically perfect but consistent with production behaviour — matching for now).

**Coalescing adjacency check**: reference checks only `Li` + `Wd` (not `Idx`). Extra `Idx` check removed.

**ResultRect / Word methods**: added `ToString()`, `ToRawString()`, `ShallowCopy()` to match the
reference's method contracts used during coalescing.

**Hit numbering**: single-word queries use element index (0-based) as hit number, matching the
reference's use of the two-argument `Select` overload.

### Text normalisation — drop vs replace

**PR review question:** Is the normalisation correct re the reference implementations?

Both Wellcome and St Louis Fed use `ToAlphanumericOrWhitespace` which **drops** non-alphanumeric characters (keeping existing whitespace), rather than replacing them with spaces. Corrected in response to review:
- `"it's"` → `"its"` (not `"it s"`)
- `"foo-bar"` → `"foobar"` (not `"foo bar"`)
- `"hello, world"` → `"hello world"` (comma dropped; adjacent space preserved)

### .gitignore fix

The standard Visual Studio `.gitignore` template includes `*.e2e` (for VS Trace files). This pattern also matched the `src/TextServices.Tests.E2E/` project directory, silently excluding it from git. Fixed with a `!*Tests.E2E/` negation rule.

---
<!-- Add new sessions below this line -->
