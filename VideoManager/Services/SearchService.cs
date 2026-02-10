using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoManager.Data;
using VideoManager.Models;

namespace VideoManager.Services;

public class SearchService : ISearchService
{
    private readonly VideoManagerDbContext _context;
    private readonly ILogger<SearchService> _logger;

    public SearchService(VideoManagerDbContext context, ILogger<SearchService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PagedResult<VideoEntry>> SearchAsync(
        SearchCriteria criteria, int page, int pageSize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), "Page must be >= 1.");
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), "PageSize must be >= 1.");

        IQueryable<VideoEntry> query = _context.VideoEntries
            .Include(v => v.Tags)
            .Include(v => v.Categories)
            .AsNoTracking();

        // Keyword: fuzzy match on Title or Description (case-insensitive)
        if (!string.IsNullOrWhiteSpace(criteria.Keyword))
        {
            var keyword = criteria.Keyword.Trim();
            query = query.Where(v =>
                EF.Functions.Like(v.Title, $"%{keyword}%") ||
                (v.Description != null && EF.Functions.Like(v.Description, $"%{keyword}%")));
        }

        // Tag filter: video must have at least one of the specified tags
        if (criteria.TagIds is { Count: > 0 })
        {
            var tagIds = criteria.TagIds;
            query = query.Where(v => v.Tags.Any(t => tagIds.Contains(t.Id)));
        }

        // Date range filter: filter by ImportedAt
        if (criteria.DateFrom.HasValue)
        {
            query = query.Where(v => v.ImportedAt >= criteria.DateFrom.Value);
        }

        if (criteria.DateTo.HasValue)
        {
            query = query.Where(v => v.ImportedAt <= criteria.DateTo.Value);
        }

        // Duration range filter
        // Query against DurationTicks (long) which is directly stored in SQLite
        // and fully translatable by EF Core.
        if (criteria.DurationMin.HasValue)
        {
            var minTicks = criteria.DurationMin.Value.Ticks;
            query = query.Where(v => v.DurationTicks >= minTicks);
        }

        if (criteria.DurationMax.HasValue)
        {
            var maxTicks = criteria.DurationMax.Value.Ticks;
            query = query.Where(v => v.DurationTicks <= maxTicks);
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync(ct);

        _logger.LogDebug("Search executed: Keyword={Keyword}, TagIds={TagIds}, DateFrom={DateFrom}, DateTo={DateTo}, TotalCount={TotalCount}.",
            criteria.Keyword, criteria.TagIds != null ? string.Join(",", criteria.TagIds) : "none",
            criteria.DateFrom, criteria.DateTo, totalCount);

        // Apply sorting based on criteria
        IOrderedQueryable<VideoEntry> orderedQuery = (criteria.SortBy, criteria.SortDir) switch
        {
            (SortField.ImportedAt, SortDirection.Ascending) => query.OrderBy(v => v.ImportedAt),
            (SortField.ImportedAt, SortDirection.Descending) => query.OrderByDescending(v => v.ImportedAt),
            (SortField.Duration, SortDirection.Ascending) => query.OrderBy(v => v.DurationTicks),
            (SortField.Duration, SortDirection.Descending) => query.OrderByDescending(v => v.DurationTicks),
            (SortField.FileSize, SortDirection.Ascending) => query.OrderBy(v => v.FileSize),
            (SortField.FileSize, SortDirection.Descending) => query.OrderByDescending(v => v.FileSize),
            _ => query.OrderByDescending(v => v.ImportedAt)
        };

        // Apply pagination
        var items = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<VideoEntry>(items, totalCount, page, pageSize);
    }
}
