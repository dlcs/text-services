using MediatR;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.PlainText;

public record RawTextRequest(string Id) : IRequest<string?>;

public class RawTextHandler(ITextStore textStore, ITextCache cache)
    : IRequestHandler<RawTextRequest, string?>
{
    public async Task<string?> Handle(RawTextRequest request, CancellationToken ct)
    {
        if (!await cache.IsEnabledAsync(request.Id, JobServices.FullText, ct)) return null;
        return await textStore.LoadRawText(request.Id);
    }
}
