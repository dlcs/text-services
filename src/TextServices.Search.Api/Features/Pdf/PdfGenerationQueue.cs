using System.Threading.Channels;
using Microsoft.Extensions.Options;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features.Pdf;

public interface IPdfGenerationQueue
{
    bool TryEnqueue(string id);
    IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct);
}

public class PdfGenerationQueue : IPdfGenerationQueue
{
    private readonly Channel<string> _channel;

    public PdfGenerationQueue(IOptions<SearchApiOptions> options)
    {
        // Wait mode: TryWrite returns false immediately when the channel is full.
        // WriteAsync would block, but we exclusively use TryWrite so this is equivalent to DropWrite
        // semantics while giving a reliable false return that lets us signal 503 to callers.
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(options.Value.PdfTriggerQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
    }

    public bool TryEnqueue(string id) => _channel.Writer.TryWrite(id);

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
