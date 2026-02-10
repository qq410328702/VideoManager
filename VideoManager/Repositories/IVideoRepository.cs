using VideoManager.Models;

namespace VideoManager.Repositories;

public interface IVideoRepository
{
    Task<VideoEntry> AddAsync(VideoEntry entry, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<VideoEntry> entries, CancellationToken ct);
    Task<VideoEntry?> GetByIdAsync(int id, CancellationToken ct);
    Task UpdateAsync(VideoEntry entry, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task<PagedResult<VideoEntry>> GetPagedAsync(int page, int pageSize, CancellationToken ct,
        SortField sortBy = SortField.ImportedAt, SortDirection sortDir = SortDirection.Descending);
}

