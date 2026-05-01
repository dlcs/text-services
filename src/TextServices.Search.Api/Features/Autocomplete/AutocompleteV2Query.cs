using MediatR;
using TextServices.Search.Api.Models;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.Autocomplete;

public record AutocompleteV2Request(string Id, string Query, string SelfUrl) : IRequest<TermPageV2?>;

public class AutocompleteV2Handler(ITextCache cache) : IRequestHandler<AutocompleteV2Request, TermPageV2?>
{
    public async Task<TermPageV2?> Handle(AutocompleteV2Request request, CancellationToken ct)
    {
        if (!await cache.IsEnabledAsync(request.Id, JobServices.Autocomplete, ct)) return null;
        if (request.Query.Trim().Length < 3)
            return new TermPageV2 { Id = request.SelfUrl };

        var ac = await cache.GetAutoCompleteAsync(request.Id, ct);
        if (ac == null) return null;

        var suggestions = ac.GetSuggestions(request.Query);

        return new TermPageV2
        {
            Id    = request.SelfUrl,
            Items = suggestions.Select(s => new TermV2 { Value = s }).ToList(),
        };
    }
}
