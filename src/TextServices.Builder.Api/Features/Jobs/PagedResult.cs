namespace TextServices.Builder.Api.Features.Jobs;

/// <summary>
/// Generic paged result wrapper returned by <c>GET /textbuilder</c>.
/// </summary>
public record PagedResult<T>(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<T> Items);
