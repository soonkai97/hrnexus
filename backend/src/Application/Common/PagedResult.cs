namespace HRNexus.Application.Common;

/// <summary>Generic paginated result wrapper returned by paged queries.</summary>
public record PagedResult<T>(List<T> Items, int Total);
