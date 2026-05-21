using MediatR;
using Microsoft.Extensions.Options;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;

namespace TextServices.Builder.Api.Features.Jobs;

public record GetJobRequest(string Id) : IRequest<JobResponse?>;

public class GetJobHandler(BuilderDbContext db, IOptions<TextServicesOptions> options)
    : IRequestHandler<GetJobRequest, JobResponse?>
{
    public async Task<JobResponse?> Handle(GetJobRequest request, CancellationToken ct)
    {
        var job = await db.Jobs.FindAsync([request.Id], ct);
        return job == null ? null : JobResponse.From(job, options.Value);
    }
}
