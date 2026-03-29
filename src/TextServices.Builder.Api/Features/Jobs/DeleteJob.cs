using Hangfire;
using MediatR;
using TextServices.Builder.Api.Data;

namespace TextServices.Builder.Api.Features.Jobs;

public record DeleteJobRequest(string Id) : IRequest<bool>;

public class DeleteJobHandler(BuilderDbContext db, IBackgroundJobClient hangfire)
    : IRequestHandler<DeleteJobRequest, bool>
{
    public async Task<bool> Handle(DeleteJobRequest request, CancellationToken ct)
    {
        var job = await db.Jobs.FindAsync([request.Id], ct);
        if (job == null) return false;

        if (job.HangfireJobId != null)
            hangfire.Delete(job.HangfireJobId);

        db.Jobs.Remove(job);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
