using VideoManager.Models;

namespace VideoManager.Repositories;

public interface ICategoryRepository
{
    Task<FolderCategory> AddAsync(FolderCategory category, CancellationToken ct);
    Task<List<FolderCategory>> GetTreeAsync(CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
