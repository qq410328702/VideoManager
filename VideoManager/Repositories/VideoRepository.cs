using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;

namespace VideoManager.Repositories;

public class VideoRepository : IVideoRepository
{
    private readonly VideoManagerDbContext _context;

    public VideoRepository(VideoManagerDbContext context)
    {
        _context = context;
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

        var items = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(v => v.Tags)
            .Include(v => v.Categories)
            .AsNoTracking()
            .ToListAsync(ct);

        return new PagedResult<VideoEntry>(items, totalCount, page, pageSize);
    }
}
