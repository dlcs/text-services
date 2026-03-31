using MediatR;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.PlainText;

/// <summary>
/// Returns the raw (un-normalised) full text for the given job ID as a plain string,
/// suitable for serving as <c>text/plain</c>.
/// </summary>
public record RawTextRequest(string Id) : IRequest<string?>;

public class RawTextHandler(ITextStore textStore)
    : IRequestHandler<RawTextRequest, string?>
{
    public async Task<string?> Handle(RawTextRequest request, CancellationToken ct)
        => await textStore.LoadRawText(request.Id);
}
