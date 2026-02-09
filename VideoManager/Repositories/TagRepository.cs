using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;

namespace VideoManager.Repositories;

public class TagRepository : ITagRepository
{
    private readonly VideoManagerDbContext _context;

    public TagRepository(VideoManagerDbContext context)
    {
        _context = context;
    }

    public async Task<Tag> AddAsync(Tag tag, CancellationToken ct)
    {
        if (await ExistsByNameAsync(tag.Name, ct))
        {
            throw new InvalidOperationException($"Tag with name '{tag.Name}' already exists.");
        }

        _context.Tags.Add(tag);
        await _context.SaveChangesAsync(ct);
        return tag;
    }

    public async Task<List<Tag>> GetAllAsync(CancellationToken ct)
    {
        return await _context.Tags
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var tag = await _context.Tags.FindAsync(new object[] { id }, ct);
        if (tag is not null)
        {
            _context.Tags.Remove(tag);
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct)
    {
        return await _context.Tags
            .AnyAsync(t => t.Name == name, ct);
    }
}
