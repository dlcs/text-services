using Hangfire;
using MediatR;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Storage;

namespace TextServices.Builder.Api.Features.Jobs;

public record DeleteJobRequest(string Id) : IRequest<bool>;

public class DeleteJobHandler(
    BuilderDbContext db,
    IBackgroundJobClient hangfire,
    ITextStore textStore,
    IHttpClientFactory httpClientFactory,
    TextServicesOptions options,
    ILogger<DeleteJobHandler> logger)
    : IRequestHandler<DeleteJobRequest, bool>
{
    public async Task<bool> Handle(DeleteJobRequest request, CancellationToken ct)
    {
        var job = await db.Jobs.FindAsync([request.Id], ct);
        if (job == null) return false;

        if (job.HangfireJobId != null)
            hangfire.Delete(job.HangfireJobId);

        await textStore.DeleteArtefacts(job.Id);

        // Notify the Search API to evict its in-process cache for this key.
        _ = InvalidateCacheAsync(job.Id);

        db.Jobs.Remove(job);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task InvalidateCacheAsync(string id)
    {
        if (string.IsNullOrEmpty(options.SearchApiBaseUrl)) return;
        try
        {
            var http = httpClientFactory.CreateClient();
            var url = $"{options.SearchApiBaseUrl.TrimEnd('/')}/cache/v1/{id}";
            await http.DeleteAsync(url);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to invalidate Search API cache for key {Id}", id);
        }
    }
}
