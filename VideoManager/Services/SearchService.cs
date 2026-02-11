using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoManager.Data;
using VideoManager.Models;

namespace VideoManager.Services;

public class SearchService : ISearchService
{
    private readonly VideoManagerDbContext _context;
    private readonly IMetricsService _metricsService;
    private readonly ILogger<SearchService> _logger;

    public SearchService(VideoManagerDbContext context, IMetricsService metricsService, ILogger<SearchService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PagedResult<VideoEntry>> SearchAsync(
        SearchCriteria criteria, int page, int pageSize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), "Page must be >= 1.");
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), "PageSize must be >= 1.");

        // Record search operation timing
        using var timer = _metricsService.StartTimer(MetricsOperationNames.Search);

        var hasKeyword = !string.IsNullOrWhiteSpace(criteria.Keyword);
        var hasTags = criteria.TagIds is { Count: > 0 };
        var hasDateFrom = criteria.DateFrom.HasValue;
        var hasDateTo = criteria.DateTo.HasValue;
        var hasDurationMin = criteria.DurationMin.HasValue;
        var hasDurationMax = criteria.DurationMax.HasValue;
        var hasAnyFilter = hasTags || hasDateFrom || hasDateTo || hasDurationMin || hasDurationMax;
        var isDefaultSort = criteria.SortBy == SortField.ImportedAt && criteria.SortDir == SortDirection.Descending;

        int skip = (page - 1) * pageSize;

        // Fast path 1: Pure keyword search with default sort (no other filters)
        if (hasKeyword && !hasAnyFilter && isDefaultSort)
        {
            try
            {
                var keyword = criteria.Keyword!.Trim();
                var items = await ToListAsync(
                    CompiledQueries.SearchByKeyword(_context, keyword, skip, pageSize), ct);

                // For total count, we still need a dynamic query since compiled queries
                // return IAsyncEnumerable and don't support CountAsync directly
                var totalCount = await GetKeywordCountAsync(keyword, ct);

                _logger.LogDebug(
                    "Search executed (compiled query - keyword): Keyword={Keyword}, TotalCount={TotalCount}.",
                    keyword, totalCount);

                return new PagedResult<VideoEntry>(items, totalCount, page, pageSize);
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                           and not ArgumentException
                                           and not ArgumentOutOfRangeException)
            {
                _logger.LogWarning(ex,
                    "Compiled query SearchByKeyword failed, falling back to dynamic LINQ.");
            }
        }

        // Fast path 2: No filters at all with default sort
        if (!hasKeyword && !hasAnyFilter && isDefaultSort)
        {
            try
            {
                var items = await ToListAsync(
                    CompiledQueries.GetPagedDefault(_context, skip, pageSize), ct);

                var totalCount = await _context.VideoEntries.CountAsync(ct);

                _logger.LogDebug(
                    "Search executed (compiled query - default paged): TotalCount={TotalCount}.",
                    totalCount);

                return new PagedResult<VideoEntry>(items, totalCount, page, pageSize);
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                           and not ArgumentException
                                           and not ArgumentOutOfRangeException)
            {
                _logger.LogWarning(ex,
                    "Compiled query GetPagedDefault failed, falling back to dynamic LINQ.");
            }
        }

        // Dynamic LINQ path: multi-condition queries or fallback from compiled query failure
        return await ExecuteDynamicQueryAsync(criteria, page, pageSize, ct);
    }

    private async Task<int> GetKeywordCountAsync(string keyword, CancellationToken ct)
    {
        return await _context.VideoEntries
            .AsNoTracking()
            .Where(v => EF.Functions.Like(v.Title, "%" + keyword + "%") ||
                        (v.Description != null && EF.Functions.Like(v.Description, "%" + keyword + "%")))
            .CountAsync(ct);
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source, CancellationToken ct)
    {
        var list = new List<T>();
        await foreach (var item in source.WithCancellation(ct))
        {
            list.Add(item);
        }
        return list;
    }

    private async Task<PagedResult<VideoEntry>> ExecuteDynamicQueryAsync(
        SearchCriteria criteria, int page, int pageSize, CancellationToken ct)
    {
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
