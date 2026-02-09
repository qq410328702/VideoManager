using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;

namespace VideoManager.Services;

/// <summary>
/// Implementation of <see cref="IEditService"/> that handles video metadata editing
/// including title/description updates and tag association management.
/// </summary>
public class EditService : IEditService
{
    private readonly VideoManagerDbContext _context;

    public EditService(VideoManagerDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<VideoEntry> UpdateVideoInfoAsync(int videoId, string title, string? description, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be null, empty, or whitespace.", nameof(title));

        var video = await _context.VideoEntries
            .Include(v => v.Tags)
            .Include(v => v.Categories)
            .FirstOrDefaultAsync(v => v.Id == videoId, ct);

        if (video is null)
            throw new KeyNotFoundException($"Video with ID {videoId} was not found.");

        video.Title = title;
        video.Description = description;

        await _context.SaveChangesAsync(ct);

        return video;
    }

    /// <inheritdoc />
    public async Task AddTagToVideoAsync(int videoId, int tagId, CancellationToken ct)
    {
        var video = await _context.VideoEntries
            .Include(v => v.Tags)
            .FirstOrDefaultAsync(v => v.Id == videoId, ct);

        if (video is null)
            throw new KeyNotFoundException($"Video with ID {videoId} was not found.");

        var tag = await _context.Tags.FindAsync(new object[] { tagId }, ct);

        if (tag is null)
            throw new KeyNotFoundException($"Tag with ID {tagId} was not found.");

        // Only add if not already associated
        if (!video.Tags.Any(t => t.Id == tagId))
        {
            video.Tags.Add(tag);
            await _context.SaveChangesAsync(ct);
        }
    }

    /// <inheritdoc />
    public async Task RemoveTagFromVideoAsync(int videoId, int tagId, CancellationToken ct)
    {
        var video = await _context.VideoEntries
            .Include(v => v.Tags)
            .FirstOrDefaultAsync(v => v.Id == videoId, ct);

        if (video is null)
            throw new KeyNotFoundException($"Video with ID {videoId} was not found.");

        var tag = video.Tags.FirstOrDefault(t => t.Id == tagId);

        if (tag is null)
            throw new KeyNotFoundException($"Tag with ID {tagId} was not found on video with ID {videoId}.");

        video.Tags.Remove(tag);
        await _context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task BatchAddTagAsync(List<int> videoIds, int tagId, CancellationToken ct)
    {
        if (videoIds is null || videoIds.Count == 0)
            throw new ArgumentException("Video IDs list cannot be null or empty.", nameof(videoIds));

        var tag = await _context.Tags.FindAsync(new object[] { tagId }, ct);
        if (tag is null)
            throw new KeyNotFoundException($"Tag with ID {tagId} was not found.");

        var videos = await _context.VideoEntries
            .Include(v => v.Tags)
            .Where(v => videoIds.Contains(v.Id))
            .ToListAsync(ct);

        foreach (var video in videos)
        {
            if (!video.Tags.Any(t => t.Id == tagId))
            {
                video.Tags.Add(tag);
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task BatchMoveToCategoryAsync(List<int> videoIds, int categoryId, CancellationToken ct)
    {
        if (videoIds is null || videoIds.Count == 0)
            throw new ArgumentException("Video IDs list cannot be null or empty.", nameof(videoIds));

        var category = await _context.FolderCategories.FindAsync(new object[] { categoryId }, ct);
        if (category is null)
            throw new KeyNotFoundException($"Category with ID {categoryId} was not found.");

        var videos = await _context.VideoEntries
            .Include(v => v.Categories)
            .Where(v => videoIds.Contains(v.Id))
            .ToListAsync(ct);

        foreach (var video in videos)
        {
            if (!video.Categories.Any(c => c.Id == categoryId))
            {
                video.Categories.Add(category);
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task UpdateTagColorAsync(int tagId, string? color, CancellationToken ct)
    {
        var tag = await _context.Tags.FindAsync(new object[] { tagId }, ct);
        if (tag is null)
            throw new KeyNotFoundException($"Tag with ID {tagId} was not found.");

        tag.Color = color;
        await _context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task AddCategoryToVideoAsync(int videoId, int categoryId, CancellationToken ct)
    {
        var video = await _context.VideoEntries
            .Include(v => v.Categories)
            .FirstOrDefaultAsync(v => v.Id == videoId, ct);

        if (video is null)
            throw new KeyNotFoundException($"Video with ID {videoId} was not found.");

        var category = await _context.FolderCategories.FindAsync(new object[] { categoryId }, ct);
        if (category is null)
            throw new KeyNotFoundException($"Category with ID {categoryId} was not found.");

        if (!video.Categories.Any(c => c.Id == categoryId))
        {
            video.Categories.Add(category);
            await _context.SaveChangesAsync(ct);
        }
    }

    /// <inheritdoc />
    public async Task RemoveCategoryFromVideoAsync(int videoId, int categoryId, CancellationToken ct)
    {
        var video = await _context.VideoEntries
            .Include(v => v.Categories)
            .FirstOrDefaultAsync(v => v.Id == videoId, ct);

        if (video is null)
            throw new KeyNotFoundException($"Video with ID {videoId} was not found.");

        var category = video.Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category is null)
            throw new KeyNotFoundException($"Category with ID {categoryId} was not found on video with ID {videoId}.");

        video.Categories.Remove(category);
        await _context.SaveChangesAsync(ct);
    }
}
