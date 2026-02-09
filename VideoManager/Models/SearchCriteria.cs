namespace VideoManager.Models;

public record SearchCriteria(
    string? Keyword,
    List<int>? TagIds,
    DateTime? DateFrom,
    DateTime? DateTo,
    TimeSpan? DurationMin,
    TimeSpan? DurationMax,
    SortField SortBy = SortField.ImportedAt,
    SortDirection SortDir = SortDirection.Descending
);

public enum SortField
{
    ImportedAt,
    Duration,
    FileSize
}

public enum SortDirection
{
    Ascending,
    Descending
}
