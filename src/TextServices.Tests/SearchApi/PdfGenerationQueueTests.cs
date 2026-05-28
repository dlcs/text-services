using Microsoft.Extensions.Options;
using Shouldly;
using TextServices.Search.Api.Configuration;
using TextServices.Search.Api.Features.Pdf;

namespace TextServices.Tests.SearchApi;

public class PdfGenerationQueueTests
{
    [Fact]
    public void TryEnqueue_UnderCapacity_ReturnsTrue()
    {
        var queue = MakeQueue(capacity: 5);

        var result = queue.TryEnqueue("test/book");

        result.ShouldBeTrue();
    }

    [Fact]
    public void TryEnqueue_AtCapacity_ReturnsFalse()
    {
        var queue = MakeQueue(capacity: 2);
        queue.TryEnqueue("a/1");
        queue.TryEnqueue("a/2");

        var result = queue.TryEnqueue("a/3");

        result.ShouldBeFalse();
    }

    private static PdfGenerationQueue MakeQueue(int capacity)
            => new(Options.Create(new SearchApiOptions { PdfTriggerQueueCapacity = capacity }));
}
