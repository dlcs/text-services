using MediatR;
using Microsoft.EntityFrameworkCore;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;

namespace TextServices.Builder.Api.Features.Jobs;

/// <param name="Page">1-based page number (default 1).</param>
/// <param name="PageSize">Items per page (default 20, max 100).</param>
/// <param name="Status">Optional status filter (e.g. "Completed", "Failed").</param>
public record ListJobsRequest(int Page, int PageSize, string? Status) : IRequest<PagedResult<JobResponse>>;

public class ListJobsHandler(BuilderDbContext db, TextServicesOptions options)
    : IRequestHandler<ListJobsRequest, PagedResult<JobResponse>>
{
    public async Task<PagedResult<JobResponse>> Handle(ListJobsRequest request, CancellationToken ct)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var query = db.Jobs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<JobStatus>(request.Status, ignoreCase: true, out var status))
        {
            query = query.Where(j => j.Status == status);
        }

        var total = await query.CountAsync(ct);

        var entities = await query
            .OrderByDescending(j => j.Created)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = entities.Select(j => JobResponse.From(j, options)).ToList();

        return new PagedResult<JobResponse>(page, pageSize, total, items);
    }
}
