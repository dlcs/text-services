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
            r.ErrorMessage == "One of sourceUri or sourceData must be provided.");
    }

    // -------------------------------------------------------------------------
    // Source — sourceUri + sourceData (indexed pdf entries)
    // -------------------------------------------------------------------------

    private const string CoverPageOnlyError =
        "When sourceUri is provided, sourceData may only contain entries with type 'pdf' and index 0.";

    private static PageInstruction CoverPage(string type = "pdf", int? index = 0,
        string? input = "https://example.org/cover.pdf") =>
        new() { Type = type, Index = index, Input = input };

    [Fact]
    public void BothSourceUriAndSourceData_NonPdfPage_FailsValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceUri = "https://example.org/manifest",
            SourceData = [Page()],
        });

        results.ShouldContain(r => r.ErrorMessage == CoverPageOnlyError);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("PDF")]
    public void BothSourceUriAndSourceData_PdfCoverPage_PassesValidation(string pdfType)
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceUri = "https://example.org/manifest",
            SourceData = [CoverPage(type: pdfType)],
        });

        results.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(null)]
    public void BothSourceUriAndSourceData_PdfWithIndexOtherThanZero_FailsValidation(int? index)
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceUri = "https://example.org/manifest",
            SourceData = [CoverPage(index: index)],
        });

        results.ShouldContain(r => r.ErrorMessage == CoverPageOnlyError);
    }

    [Fact]
    public void BothSourceUriAndSourceData_CustomType_FailsValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceUri = "https://example.org/manifest",
            SourceData = [CoverPage(type: "redacted")],
        });

        results.ShouldContain(r => r.ErrorMessage == CoverPageOnlyError);
    }

    [Fact]
    public void BothSourceUriAndSourceData_DuplicateIndex_FailsValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceUri = "https://example.org/manifest",
            SourceData = [CoverPage(), CoverPage(input: "https://example.org/disclaimer.pdf")],
        });

        results.ShouldContain(r => r.ErrorMessage == "Each 'index' in sourceData must be unique.");
    }

    [Fact]
    public void SourceDataOnly_DuplicateIndex_FailsValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceData = [CoverPage(index: 2), Page(), CoverPage(index: 2)],
        });

        results.ShouldContain(r => r.ErrorMessage == "Each 'index' in sourceData must be unique.");
    }

    // -------------------------------------------------------------------------
    // SourceData only — index all-or-nothing
    // -------------------------------------------------------------------------

    private const string IndexAllOrNothingError =
        "If 'index' is set on any sourceData entry, it must be set on all entries.";

    private static PageInstruction IndexedPage(int? index, string id = "https://example.org/canvas/1") =>
        new() { Id = id, Index = index };

    [Fact]
    public void SourceDataOnly_IndexSetOnSomeEntries_FailsValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceData = [IndexedPage(1), IndexedPage(null, "https://example.org/canvas/2")],
        });

        results.ShouldContain(r => r.ErrorMessage == IndexAllOrNothingError);
    }

    [Fact]
    public void SourceDataOnly_IndexSetOnAllEntries_PassesValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceData = [IndexedPage(2), IndexedPage(1, "https://example.org/canvas/2"), CoverPage(index: 0)],
        });

        results.ShouldBeEmpty();
    }

    [Fact]
    public void SourceDataOnly_IndexSetOnNoEntries_PassesValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceData = [Page(), Page(id: null, type: "pdf")],
        });

        results.ShouldBeEmpty();
    }

    [Fact]
    public void BothSourceUriAndSourceData_CoverPageWithoutInput_FailsValidation()
    {
        var results = Validate(new JobInstruction
        {
            Id = "my-job",
            SourceUri = "https://example.org/manifest",
            SourceData = [CoverPage(input: null)],
        });

        results.ShouldContain(r =>
            r.ErrorMessage == "Each pdf page in sourceData must supply an 'input' (PDF URI).");
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
            r.ErrorMessage == "One of sourceUri or sourceData must be provided.");
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
