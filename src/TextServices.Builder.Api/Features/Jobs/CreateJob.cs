using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Jobs;

namespace TextServices.Builder.Api.Features.Jobs;

public record CreateJobRequest(JobInstruction Instruction) : IRequest<CreateJobResult>;

public record CreateJobResult(bool AlreadyExists, JobResponse Response);

public class CreateJobHandler(
    BuilderDbContext db,
    IBackgroundJobClient hangfire,
    TextServicesOptions options)
    : IRequestHandler<CreateJobRequest, CreateJobResult>
{
    public async Task<CreateJobResult> Handle(CreateJobRequest request, CancellationToken ct)
    {
        var instruction = request.Instruction;

        if (await db.Jobs.AnyAsync(j => j.Id == instruction.Id, ct))
        {
            var existing = (await db.Jobs.FindAsync([instruction.Id], ct))!;
            return new CreateJobResult(true, JobResponse.From(existing, options));
        }

        string? sourceDataJson = null;
        if (instruction.SourceData != null)
        {
            sourceDataJson = System.Text.Json.JsonSerializer.Serialize(instruction.SourceData);
        }

        var job = new BuilderJob
        {
            Id             = instruction.Id,
            SourceUri      = instruction.SourceUri,
            SourceDataJson = sourceDataJson,
            Status         = JobStatus.Waiting,
            Created        = DateTimeOffset.UtcNow,
            Services       = (int)instruction.Services,
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);

        var hangfireJobId = hangfire.Enqueue<TextBuildJob>(j => j.ExecuteAsync(job.Id, null!));
        job.HangfireJobId = hangfireJobId;
        await db.SaveChangesAsync(ct);

        return new CreateJobResult(false, JobResponse.From(job, options));
    }
}
