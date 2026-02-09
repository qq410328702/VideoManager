using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;

namespace VideoManager.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly VideoManagerDbContext _context;

    public CategoryRepository(VideoManagerDbContext context)
    {
        _context = context;
    }

    public async Task<FolderCategory> AddAsync(FolderCategory category, CancellationToken ct)
    {
        _context.FolderCategories.Add(category);
        await _context.SaveChangesAsync(ct);
        return category;
    }

    public async Task<List<FolderCategory>> GetTreeAsync(CancellationToken ct)
    {
        // Load all categories with their children eagerly loaded
        var allCategories = await _context.FolderCategories
            .Include(c => c.Children)
            .ToListAsync(ct);

        // Return only root nodes (ParentId == null); EF Core's change tracker
        // automatically wires up the Children navigation properties for all levels
        return allCategories.Where(c => c.ParentId == null).ToList();
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var category = await _context.FolderCategories
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is not null)
        {
            // Remove associations with videos (many-to-many) but keep the videos themselves
            // EF Core cascade delete on parent-child handles child categories automatically
            _context.FolderCategories.Remove(category);
            await _context.SaveChangesAsync(ct);
        }
    }
}
