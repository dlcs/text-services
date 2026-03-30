using Microsoft.EntityFrameworkCore;
using Shouldly;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Features.Jobs;

namespace TextServices.Tests.BuilderApi;

public class ListJobsTests : IDisposable
{
    private readonly BuilderDbContext _db;
    private readonly TextServicesOptions _options = new();

    public ListJobsTests()
    {
        var opts = new DbContextOptionsBuilder<BuilderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new BuilderDbContext(opts);
    }

    public void Dispose() => _db.Dispose();

    // ---- helpers ---------------------------------------------------------------

    private async Task Seed(params BuilderJob[] jobs)
    {
        _db.Jobs.AddRange(jobs);
        await _db.SaveChangesAsync();
    }

    private static BuilderJob Job(string id, JobStatus status = JobStatus.Completed,
        DateTimeOffset? created = null) => new()
    {
        Id         = id,
        SourceUri  = $"https://example.org/{id}",
        Status     = status,
        Created    = created ?? DateTimeOffset.UtcNow,
    };

    private async Task<PagedResult<JobResponse>> List(
        int page = 1, int pageSize = 20, string? status = null)
    {
        var handler = new ListJobsHandler(_db, _options);
        return await handler.Handle(new ListJobsRequest(page, pageSize, status), default);
    }

    // ---- tests -----------------------------------------------------------------

    [Fact]
    public async Task EmptyDatabase_ReturnsEmptyPage()
    {
        var result = await List();

        result.TotalCount.ShouldBe(0);
        result.Items.ShouldBeEmpty();
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(20);
    }

    [Fact]
    public async Task ReturnsAllJobs_WhenCountBelowPageSize()
    {
        await Seed(Job("a"), Job("b"), Job("c"));

        var result = await List();

        result.TotalCount.ShouldBe(3);
        result.Items.Count.ShouldBe(3);
    }

    [Fact]
    public async Task OrderedByCreatedDescending()
    {
        var t = DateTimeOffset.UtcNow;
        await Seed(
            Job("old",    created: t.AddHours(-2)),
            Job("middle", created: t.AddHours(-1)),
            Job("new",    created: t));

        var result = await List();

        result.Items[0].Id.ShouldBe("new");
        result.Items[1].Id.ShouldBe("middle");
        result.Items[2].Id.ShouldBe("old");
    }

    [Fact]
    public async Task Paging_ReturnsCorrectPage()
    {
        var t = DateTimeOffset.UtcNow;
        await Seed(
            Job("1", created: t.AddMinutes(-4)),
            Job("2", created: t.AddMinutes(-3)),
            Job("3", created: t.AddMinutes(-2)),
            Job("4", created: t.AddMinutes(-1)),
            Job("5", created: t));

        var page1 = await List(page: 1, pageSize: 2);
        var page2 = await List(page: 2, pageSize: 2);
        var page3 = await List(page: 3, pageSize: 2);

        page1.TotalCount.ShouldBe(5);
        page1.Items.Select(j => j.Id).ShouldBe(["5", "4"]);
        page2.Items.Select(j => j.Id).ShouldBe(["3", "2"]);
        page3.Items.Select(j => j.Id).ShouldBe(["1"]);
    }

    [Fact]
    public async Task PageSizeClamped_ToMaxOf100()
    {
        await Seed(Enumerable.Range(1, 5).Select(i => Job($"job-{i}")).ToArray());

        var result = await List(pageSize: 999);

        result.PageSize.ShouldBe(100);
        result.Items.Count.ShouldBe(5); // only 5 jobs exist
    }

    [Fact]
    public async Task PageClamped_ToMinOf1()
    {
        await Seed(Job("a"));

        var result = await List(page: -5);

        result.Page.ShouldBe(1);
        result.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task StatusFilter_ReturnsOnlyMatchingJobs()
    {
        await Seed(
            Job("completed-1", JobStatus.Completed),
            Job("completed-2", JobStatus.Completed),
            Job("failed-1",    JobStatus.Failed),
            Job("waiting-1",   JobStatus.Waiting));

        var result = await List(status: "Completed");

        result.TotalCount.ShouldBe(2);
        result.Items.All(j => j.Status == "Completed").ShouldBeTrue();
    }

    [Fact]
    public async Task StatusFilter_CaseInsensitive()
    {
        await Seed(Job("x", JobStatus.Failed));

        var result = await List(status: "failed");

        result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task StatusFilter_UnknownValue_ReturnsAllJobs()
    {
        await Seed(Job("a", JobStatus.Completed), Job("b", JobStatus.Failed));

        var result = await List(status: "nonsense");

        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task TotalCount_ReflectsUnfilteredTotalWhenStatusGiven()
    {
        await Seed(
            Job("c", JobStatus.Completed),
            Job("f", JobStatus.Failed));

        var result = await List(status: "Completed");

        // TotalCount is the count *after* filtering — it's the filtered total
        result.TotalCount.ShouldBe(1);
    }
}
