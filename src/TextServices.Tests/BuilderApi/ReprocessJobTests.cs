using System.Text.Json;
using FakeItEasy;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Storage;

namespace TextServices.Tests.BuilderApi;

public sealed class ReprocessJobTests : IDisposable
{
    private const string SourceUri = "https://example.org/manifest";
    private const string Title = "Old title";
    private const JobServices Services = JobServices.Search;

    private readonly BuilderDbContext _db;
    private readonly ITextStore _textStore = A.Fake<ITextStore>();

    public ReprocessJobTests()
    {
        var options = new DbContextOptionsBuilder<BuilderDbContext>()
            .UseInMemoryDatabase($"ReprocessJob_{Guid.NewGuid():N}")
            .Options;
        _db = new BuilderDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_WithoutInstruction_KeepsStoredDefinition()
    {
        var job = await CreateCompletedJob();

        var result = await MakeHandler().Handle(new ReprocessJobRequest(job.Id), CancellationToken.None);

        result.Status.ShouldBe(ReprocessStatus.Ok);
        var updated = (await _db.Jobs.FindAsync(job.Id))!;
        updated.SourceUri.ShouldBe(SourceUri);
        updated.SourceDataJson.ShouldBe(job.SourceDataJson);
        updated.Services.ShouldBe((int)Services);
        updated.Title.ShouldBe(Title);
        updated.Status.ShouldBe(JobStatus.Waiting);
        updated.InvocationCount.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_WithInstruction_ReplacesInsertedPdfs()
    {
        var job = await CreateCompletedJob();
        var instruction = MatchingInstruction(job.Id);
        instruction.SourceData =
            [new PageInstruction { Type = "pdf", Index = 0, Input = "https://example.org/new-cover.pdf" }];

        var result = await MakeHandler().Handle(new ReprocessJobRequest(job.Id, instruction), CancellationToken.None);

        result.Status.ShouldBe(ReprocessStatus.Ok);
        var updated = (await _db.Jobs.FindAsync(job.Id))!;
        var pages = JsonSerializer.Deserialize<List<PageInstruction>>(updated.SourceDataJson!)!;
        pages.ShouldHaveSingleItem().Input.ShouldBe("https://example.org/new-cover.pdf");
        updated.InvocationCount.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_WithInstructionWithoutSourceData_RemovesInsertedPdfs()
    {
        var job = await CreateCompletedJob();

        var result = await MakeHandler().Handle(
            new ReprocessJobRequest(job.Id, MatchingInstruction(job.Id)), CancellationToken.None);

        result.Status.ShouldBe(ReprocessStatus.Ok);
        var updated = (await _db.Jobs.FindAsync(job.Id))!;
        updated.SourceDataJson.ShouldBeNull();
        updated.Services.ShouldBe((int)Services);
        updated.Title.ShouldBe(Title);
    }

    [Fact]
    public async Task Handle_WithInstructionChangingOtherFields_ReturnsInvalidAndKeepsJob()
    {
        var job = await CreateCompletedJob();
        var instruction = new JobInstruction
        {
            Id = job.Id,
            SourceUri = "https://example.org/manifest-v2",
            Services = JobServices.All,
            Title = "New title",
            CustomTypes = new() { ["redacted"] = new CustomPageType { Message = "Redacted" } },
        };

        var result = await MakeHandler().Handle(new ReprocessJobRequest(job.Id, instruction), CancellationToken.None);

        result.Status.ShouldBe(ReprocessStatus.Invalid);
        result.Errors.ShouldBe(
        [
            "sourceUri cannot be changed.",
            "services cannot be changed.",
            "title cannot be changed.",
            "customTypes cannot be changed.",
        ]);
        var updated = (await _db.Jobs.FindAsync(job.Id))!;
        updated.SourceDataJson.ShouldBe(job.SourceDataJson);
        updated.Status.ShouldBe(JobStatus.Completed);
        updated.InvocationCount.ShouldBe(1);
        A.CallTo(() => _textStore.DeleteArtefacts(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Handle_SourceDataJob_WithInstruction_ReturnsInvalid()
    {
        var job = new BuilderJob
        {
            Id = "test/reprocess-sourcedata",
            SourceDataJson = JsonSerializer.Serialize(new List<PageInstruction>
            {
                new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500 },
            }),
            Status = JobStatus.Completed,
            InvocationCount = 1,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var instruction = new JobInstruction
        {
            Id = job.Id,
            SourceData = [new PageInstruction { Id = "https://example.org/c/2", Width = 1000, Height = 1500 }],
        };

        var result = await MakeHandler().Handle(new ReprocessJobRequest(job.Id, instruction), CancellationToken.None);

        result.Status.ShouldBe(ReprocessStatus.Invalid);
        result.Errors!.ShouldHaveSingleItem().ShouldStartWith("Only jobs created from a sourceUri can be updated.");
        (await _db.Jobs.FindAsync(job.Id))!.InvocationCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_RunningJob_WithInstruction_ReturnsConflictAndKeepsDefinition()
    {
        var job = await CreateCompletedJob(JobStatus.Running);

        var result = await MakeHandler().Handle(
            new ReprocessJobRequest(job.Id, MatchingInstruction(job.Id)), CancellationToken.None);

        result.Status.ShouldBe(ReprocessStatus.Conflict);
        var updated = (await _db.Jobs.FindAsync(job.Id))!;
        updated.SourceDataJson.ShouldBe(job.SourceDataJson);
        updated.InvocationCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_UnknownJob_ReturnsNotFound()
    {
        var result = await MakeHandler().Handle(
            new ReprocessJobRequest("missing", MatchingInstruction("missing")), CancellationToken.None);

        result.Status.ShouldBe(ReprocessStatus.NotFound);
    }

    /// <summary>An instruction matching the stored job in every field except sourceData.</summary>
    private static JobInstruction MatchingInstruction(string id) =>
        new() { Id = id, SourceUri = SourceUri, Services = Services, Title = Title };

    private async Task<BuilderJob> CreateCompletedJob(JobStatus status = JobStatus.Completed)
    {
        // SourceDataJson as left by a previous run: inserted pdf followed by reduced canvases.
        var job = new BuilderJob
        {
            Id = "test/reprocess",
            SourceUri = SourceUri,
            SourceDataJson = JsonSerializer.Serialize(new List<PageInstruction>
            {
                new() { Type = "pdf", Index = 0, Input = "https://example.org/cover.pdf" },
                new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500 },
            }),
            Services = (int)Services,
            Title = Title,
            Status = status,
            InvocationCount = 1,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private ReprocessJobHandler MakeHandler() =>
        new(_db,
            A.Fake<IBackgroundJobClient>(),
            _textStore,
            A.Fake<IHttpClientFactory>(),
            Options.Create(new TextServicesOptions()),
            NullLogger<ReprocessJobHandler>.Instance);
}
