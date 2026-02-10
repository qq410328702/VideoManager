using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoManager.Data;
using VideoManager.Models;

namespace VideoManager.Repositories;

public class VideoRepository : IVideoRepository
{
    private readonly VideoManagerDbContext _context;
    private readonly ILogger<VideoRepository> _logger;

    public VideoRepository(VideoManagerDbContext context, ILogger<VideoRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<VideoEntry> AddAsync(VideoEntry entry, CancellationToken ct)
    {
        _context.VideoEntries.Add(entry);
        await _context.SaveChangesAsync(ct);
        return entry;
    }

    public async Task AddRangeAsync(IEnumerable<VideoEntry> entries, CancellationToken ct)
    {
        _context.VideoEntries.AddRange(entries);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<VideoEntry?> GetByIdAsync(int id, CancellationToken ct)
    {
        return await _context.VideoEntries
            .Include(v => v.Tags)
            .Include(v => v.Categories)
            .FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task UpdateAsync(VideoEntry entry, CancellationToken ct)
    {
        _context.VideoEntries.Update(entry);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var entry = await _context.VideoEntries.FindAsync(new object[] { id }, ct);
        if (entry is not null)
        {
            _context.VideoEntries.Remove(entry);
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<PagedResult<VideoEntry>> GetPagedAsync(int page, int pageSize, CancellationToken ct,
        SortField sortBy = SortField.ImportedAt, SortDirection sortDir = SortDirection.Descending)
    {
        var totalCount = await _context.VideoEntries.CountAsync(ct);

        var isDefaultSort = sortBy == SortField.ImportedAt && sortDir == SortDirection.Descending;
        int skip = (page - 1) * pageSize;

        // Fast path: default sort (ImportedAt Descending) uses compiled query
        if (isDefaultSort)
        {
            try
            {
                var items = await ToListAsync(
                    CompiledQueries.GetPagedDefault(_context, skip, pageSize), ct);

                _logger.LogDebug(
                    "GetPagedAsync executed (compiled query): Page={Page}, PageSize={PageSize}, TotalCount={TotalCount}.",
                    page, pageSize, totalCount);

                return new PagedResult<VideoEntry>(items, totalCount, page, pageSize);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Compiled query GetPagedDefault failed, falling back to dynamic LINQ.");
            }
        }

        // Dynamic LINQ path: non-default sort or fallback from compiled query failure
        IQueryable<VideoEntry> query = _context.VideoEntries;

        IOrderedQueryable<VideoEntry> orderedQuery = (sortBy, sortDir) switch
        {
            (SortField.ImportedAt, SortDirection.Ascending) => query.OrderBy(v => v.ImportedAt),
            (SortField.ImportedAt, SortDirection.Descending) => query.OrderByDescending(v => v.ImportedAt),
            (SortField.Duration, SortDirection.Ascending) => query.OrderBy(v => v.DurationTicks),
            (SortField.Duration, SortDirection.Descending) => query.OrderByDescending(v => v.DurationTicks),
            (SortField.FileSize, SortDirection.Ascending) => query.OrderBy(v => v.FileSize),
            (SortField.FileSize, SortDirection.Descending) => query.OrderByDescending(v => v.FileSize),
            _ => query.OrderByDescending(v => v.ImportedAt)
        };

        var dynamicItems = await orderedQuery
            .Skip(skip)
            .Take(pageSize)
            .Include(v => v.Tags)
            .Include(v => v.Categories)
            .AsNoTracking()
            .ToListAsync(ct);

        return new PagedResult<VideoEntry>(dynamicItems, totalCount, page, pageSize);
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
}
