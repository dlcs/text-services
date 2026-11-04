using Shouldly;
using TextServices.Storage;

namespace TextServices.Tests.Storage;

public class JobServicesTests
{
    [Theory]
    [InlineData(JobServices.Autocomplete, JobServices.Search | JobServices.Autocomplete)]
    [InlineData(JobServices.Autocomplete | JobServices.Pdf,
        JobServices.Search | JobServices.Autocomplete | JobServices.Pdf)]
    [InlineData(JobServices.Search, JobServices.Search)]
    [InlineData(JobServices.Search | JobServices.Autocomplete, JobServices.Search | JobServices.Autocomplete)]
    [InlineData(JobServices.Pdf, JobServices.Pdf)]
    [InlineData(JobServices.None, JobServices.None)]
    [InlineData(JobServices.All, JobServices.All)]
    public void WithImpliedServices_AddsSearchWhenAutocompleteRequested(JobServices requested, JobServices expected)
        => requested.WithImpliedServices().ShouldBe(expected);
}
