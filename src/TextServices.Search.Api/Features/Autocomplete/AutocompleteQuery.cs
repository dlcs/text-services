using MediatR;
using TextServices.Search.Api.Models;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.Autocomplete;

public record AutocompleteRequest(string Id, string Query, string SelfUrl) : IRequest<AutocompleteTermList?>;

public class AutocompleteHandler(ITextCache cache) : IRequestHandler<AutocompleteRequest, AutocompleteTermList?>
{
    public async Task<AutocompleteTermList?> Handle(AutocompleteRequest request, CancellationToken ct)
    {
        if (!await cache.IsEnabledAsync(request.Id, JobServices.Autocomplete, ct)) return null;

        // Queries shorter than 3 characters cannot match any bucket prefix —
        // return an empty term list (not a 404) as the resource exists.
        if (request.Query.Trim().Length < 3)
            return new AutocompleteTermList { Id = request.SelfUrl };

        var ac = await cache.GetAutoCompleteAsync(request.Id, ct);
        if (ac == null) return null;

        var suggestions = ac.GetSuggestions(request.Query);

        return new AutocompleteTermList
        {
            Id = request.SelfUrl,
            Terms = suggestions.Select(s => new AutocompleteTerm { Match = s }).ToList(),
        };
    }
}
