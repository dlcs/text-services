using System.ComponentModel.DataAnnotations;
using Shouldly;
using TextServices.Builder.Api.Features.Jobs;

namespace TextServices.Tests.BuilderApi;

public class JobInstructionTests
{
    private static List<ValidationResult> Validate(JobInstruction instruction)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instruction, new ValidationContext(instruction), results, validateAllProperties: true);
        return results;
    }

    private static PageInstruction Page(string? id = "https://example.org/canvas/1", string? type = null) =>
        new() { Id = id, Type = type };

    // -------------------------------------------------------------------------
    // Id — leading slash
    // -------------------------------------------------------------------------

    [Fact]
    public void Id_StartingWithSlash_FailsValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "/bad-id",
            SourceUri = "https://example.org/manifest",
        });

        results.ShouldContain(r =>
            r.MemberNames.Contains(nameof(JobInstruction.Id)) &&
            r.ErrorMessage == "Job ID must not start with '/'.");
    }

    [Theory]
    [InlineData("my-job")]
    [InlineData("2/books/my-book")]
    [InlineData("customer/collection/item")]
    public void Id_NotStartingWithSlash_NoIdError(string id)
    {
        var results = Validate(new JobInstruction
        {
            Id = id,
            SourceUri = "https://example.org/manifest",
        });

        results.ShouldNotContain(r => r.MemberNames.Contains(nameof(JobInstruction.Id)));
    }

    // -------------------------------------------------------------------------
    // Source — neither / both / one
    // -------------------------------------------------------------------------

    [Fact]
    public void NeitherSourceUriNorSourceData_FailsValidation()
    {
        var results = Validate(new JobInstruction { Id = "my-job" });

        results.ShouldContain(r =>
            r.ErrorMessage == "Exactly one of sourceUri or sourceData must be provided.");
    }

    [Fact]
    public void BothSourceUriAndSourceData_FailsValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceUri = "https://example.org/manifest",
            SourceData = [Page()],
        });

        results.ShouldContain(r =>
            r.ErrorMessage == "Provide sourceUri or sourceData, not both.");
    }

    [Fact]
    public void OnlySourceUri_PassesSourceValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceUri = "https://example.org/manifest",
        });

        results.ShouldNotContain(r =>
            r.MemberNames.Contains(nameof(JobInstruction.SourceUri)) ||
            r.MemberNames.Contains(nameof(JobInstruction.SourceData)));
    }

    [Fact]
    public void OnlySourceData_PassesSourceValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceData = [Page()],
        });

        results.ShouldNotContain(r =>
            r.MemberNames.Contains(nameof(JobInstruction.SourceUri)) ||
            r.MemberNames.Contains(nameof(JobInstruction.SourceData)));
    }

    [Fact]
    public void EmptySourceDataList_TreatedAsNoSource()
    {
        // Count: 0 means hasData is false — same as omitting SourceData entirely.
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceData = [],
        });

        results.ShouldContain(r =>
            r.ErrorMessage == "Exactly one of sourceUri or sourceData must be provided.");
    }

    // -------------------------------------------------------------------------
    // SourceData — per-page id requirement
    // -------------------------------------------------------------------------

    [Fact]
    public void SourceData_NonPdfPageWithoutId_FailsValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceData = [Page(id: null)],
        });

        results.ShouldContain(r =>
            r.MemberNames.Contains(nameof(JobInstruction.SourceData)) &&
            r.ErrorMessage == "Each non-pdf page in sourceData must supply an 'id' (canvas identifier URI).");
    }

    [Fact]
    public void SourceData_NonPdfPageWithId_PassesValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceData = [Page(id: "https://example.org/canvas/1")],
        });

        results.ShouldNotContain(r =>
            r.ErrorMessage == "Each non-pdf page in sourceData must supply an 'id' (canvas identifier URI).");
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("PDF")]
    [InlineData("Pdf")]
    public void SourceData_PdfPageWithoutId_PassesValidation(string pdfType)
    {
        // pdf-type pages are exempt from the id requirement.
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceData = [Page(id: null, type: pdfType)],
        });

        results.ShouldNotContain(r =>
            r.ErrorMessage == "Each non-pdf page in sourceData must supply an 'id' (canvas identifier URI).");
    }

    [Fact]
    public void SourceData_MixedPages_OnlyPagesMissingIdFail()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceData =
            [
                Page(id: "https://example.org/canvas/1"),
                Page(id: null),                           // should fail
                Page(id: null, type: "pdf"),              // exempt
                Page(id: "https://example.org/canvas/3"),
            ],
        });

        // Exactly one error for the page missing its id.
        results.Count(r =>
            r.ErrorMessage == "Each non-pdf page in sourceData must supply an 'id' (canvas identifier URI).")
            .ShouldBe(1);
    }
}
