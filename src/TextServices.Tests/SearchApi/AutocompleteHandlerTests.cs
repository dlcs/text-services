using Shouldly;
using TextServices.Core.Models;
using TextServices.Search.Api.Features.Autocomplete;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Tests.SearchApi;

public class AutocompleteHandlerTests
{
    private const string SelfUrl = "https://search.example.org/autocomplete/v1/test/book";

    [Fact]
    public async Task Handle_ShortQuery_ReturnsEmptyTermList()
    {
        var handler = new AutocompleteHandler(new StubTextCache(MakeAutoComplete()));

        // Queries < 3 chars return empty list, not null.
        var result = await handler.Handle(
            new AutocompleteRequest("test/book", "ab", SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Terms.ShouldBeEmpty();
        result.Id.ShouldBe(SelfUrl);
    }

    [Fact]
    public async Task Handle_AutoCompleteNotFound_ReturnsNull()
    {
        var handler = new AutocompleteHandler(new StubTextCache(null));

        var result = await handler.Handle(
            new AutocompleteRequest("missing/book", "par", SelfUrl), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_MatchingPrefix_ReturnsSuggestions()
    {
        var ac = MakeAutoComplete("parliament", "parliamentary", "parish", "other");
        var handler = new AutocompleteHandler(new StubTextCache(ac));

        var result = await handler.Handle(
            new AutocompleteRequest("test/book", "par", SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Terms.Count.ShouldBe(3);
        result.Terms.Select(t => t.Match).ShouldContain("parliament");
        result.Terms.Select(t => t.Match).ShouldContain("parliamentary");
        result.Terms.Select(t => t.Match).ShouldContain("parish");
    }

    [Fact]
    public async Task Handle_TermsHaveCorrectType()
    {
        var ac = MakeAutoComplete("parliament");
        var handler = new AutocompleteHandler(new StubTextCache(ac));

        var result = await handler.Handle(
            new AutocompleteRequest("test/book", "par", SelfUrl), CancellationToken.None);

        result!.Type.ShouldBe("search:TermList");
        result.Context.ShouldBe("http://iiif.io/api/search/1/context.json");
        result.Terms[0].Type.ShouldBe("search:Term");
    }

    [Fact]
    public async Task Handle_SuggestionsOrderedByLengthThenAlpha()
    {
        var ac = MakeAutoComplete("parliamentary", "parish", "parliament");
        var handler = new AutocompleteHandler(new StubTextCache(ac));

        var result = await handler.Handle(
            new AutocompleteRequest("test/book", "par", SelfUrl), CancellationToken.None);

        var matches = result!.Terms.Select(t => t.Match).ToList();
        matches[0].ShouldBe("parish");        // shortest
        matches[1].ShouldBe("parliament");    // then alpha
        matches[2].ShouldBe("parliamentary");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static AutoComplete MakeAutoComplete(params string[] words)
    {
        var buckets = new Dictionary<string, HashSet<string>>();
        foreach (var word in words)
        {
            if (word.Length >= 3)
            {
                var prefix = word[..3];
                if (!buckets.TryGetValue(prefix, out var set))
                    buckets[prefix] = set = [];
                set.Add(word);
            }
        }
        return new AutoComplete { Buckets = buckets };
    }

    private sealed class StubTextCache(AutoComplete? ac) : ITextCache
    {
        public Task<Text?> GetTextAsync(string key, CancellationToken ct = default)
            => Task.FromResult<Text?>(null);

        public Task<AutoComplete?> GetAutoCompleteAsync(string key, CancellationToken ct = default)
            => Task.FromResult(ac);
        public Task<JobServices?> GetCapabilitiesAsync(string key, CancellationToken ct = default)
            => Task.FromResult<JobServices?>(null);
        public void Invalidate(string key) { }
    }
}
