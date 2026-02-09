using VideoManager.Models;

namespace VideoManager.Repositories;

public interface ITagRepository
{
    Task<Tag> AddAsync(Tag tag, CancellationToken ct);
    Task<List<Tag>> GetAllAsync(CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct);
}
