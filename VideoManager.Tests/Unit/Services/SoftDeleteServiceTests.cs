using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Unit tests for soft delete behavior in DeleteService.
/// Uses SQLite In-Memory provider with explicit connection management
/// to ensure the global query filter (HasQueryFilter(v => !v.IsDeleted)) is active.
/// </summary>
public class SoftDeleteServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly VideoManagerDbContext _context;
    private readonly DeleteService _service;

    public SoftDeleteServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new VideoManagerDbContext(options);
        _context.Database.EnsureCreated();

        _service = new DeleteService(_context, NullLogger<DeleteService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>
    /// Helper to add a video entry to the database.
    /// </summary>
    private async Task<VideoEntry> AddVideoAsync(
        string title = "Test Video",
        string? filePath = null,
        string? thumbnailPath = null)
    {
        var video = new VideoEntry
        {
            Title = title,
            FileName = $"{title.Replace(" ", "_").ToLower()}.mp4",
            FilePath = filePath ?? $"/videos/{title.Replace(" ", "_").ToLower()}.mp4",
            ThumbnailPath = thumbnailPath,
            FileSize = 1024 * 1024,
            Duration = TimeSpan.FromMinutes(5),
            Width = 1920,
            Height = 1080,
            ImportedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.VideoEntries.Add(video);
        await _context.SaveChangesAsync();
        return video;
    }

    private async Task<Tag> AddTagAsync(string name)
    {
        var tag = new Tag { Name = name };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();
        return tag;
    }

    private async Task<FolderCategory> AddCategoryAsync(string name)
    {
        var category = new FolderCategory { Name = name };
        _context.FolderCategories.Add(category);
        await _context.SaveChangesAsync();
        return category;
    }

    #region 1. After soft delete, IsDeleted is true and DeletedAt is not null

    [Fact]
    public async Task SoftDelete_SetsIsDeletedTrue_And_DeletedAtNotNull()
    {
        var video = await AddVideoAsync();

        await _service.DeleteVideoAsync(video.Id, deleteFile: false, CancellationToken.None);

        // Must use IgnoreQueryFilters because the global filter excludes IsDeleted=true records
        var deletedVideo = await _context.VideoEntries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == video.Id);

        Assert.NotNull(deletedVideo);
        Assert.True(deletedVideo.IsDeleted);
        Assert.NotNull(deletedVideo.DeletedAt);
    }

    #endregion

    #region 2. After soft delete, Tags and Categories collections are cleared

    [Fact]
    public async Task SoftDelete_ClearsTagAssociations()
    {
        var video = await AddVideoAsync();
        var tag1 = await AddTagAsync("Action");
        var tag2 = await AddTagAsync("Comedy");
        video.Tags.Add(tag1);
        video.Tags.Add(tag2);
        await _context.SaveChangesAsync();

        await _service.DeleteVideoAsync(video.Id, deleteFile: false, CancellationToken.None);

        // The video should still exist in DB (soft deleted) but with no tag associations
        var deletedVideo = await _context.VideoEntries
            .IgnoreQueryFilters()
            .Include(v => v.Tags)
            .FirstOrDefaultAsync(v => v.Id == video.Id);

        Assert.NotNull(deletedVideo);
        Assert.Empty(deletedVideo.Tags);

        // Tags themselves should still exist
        Assert.True(await _context.Tags.AnyAsync(t => t.Id == tag1.Id));
        Assert.True(await _context.Tags.AnyAsync(t => t.Id == tag2.Id));
    }

    [Fact]
    public async Task SoftDelete_ClearsCategoryAssociations()
    {
        var video = await AddVideoAsync();
        var category = await AddCategoryAsync("Movies");
        video.Categories.Add(category);
        await _context.SaveChangesAsync();

        await _service.DeleteVideoAsync(video.Id, deleteFile: false, CancellationToken.None);

        var deletedVideo = await _context.VideoEntries
            .IgnoreQueryFilters()
            .Include(v => v.Categories)
            .FirstOrDefaultAsync(v => v.Id == video.Id);

        Assert.NotNull(deletedVideo);
        Assert.Empty(deletedVideo.Categories);

        // Category itself should still exist
        Assert.True(await _context.FolderCategories.AnyAsync(c => c.Id == category.Id));
    }

    #endregion

    #region 3. After soft delete, the video is NOT physically removed from the database

    [Fact]
    public async Task SoftDelete_DoesNotPhysicallyRemoveRecord()
    {
        var video = await AddVideoAsync();

        await _service.DeleteVideoAsync(video.Id, deleteFile: false, CancellationToken.None);

        // Using IgnoreQueryFilters to bypass the global filter — the record should still exist
        var exists = await _context.VideoEntries
            .IgnoreQueryFilters()
            .AnyAsync(v => v.Id == video.Id);

        Assert.True(exists);
    }

    #endregion

    #region 4. After soft delete, the video is NOT visible in regular queries

    [Fact]
    public async Task SoftDelete_VideoNotVisibleInRegularQueries()
    {
        var video = await AddVideoAsync();

        await _service.DeleteVideoAsync(video.Id, deleteFile: false, CancellationToken.None);

        // Regular query (with global filter active) should NOT find the soft-deleted video
        var visibleInRegularQuery = await _context.VideoEntries
            .AnyAsync(v => v.Id == video.Id);

        Assert.False(visibleInRegularQuery);
    }

    #endregion

    #region 5. When deleteFile is true, physical files are still deleted from disk

    [Fact]
    public async Task SoftDelete_WithDeleteFileTrue_DeletesPhysicalFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var videoFile = Path.Combine(tempDir, "test_video.mp4");
            var thumbFile = Path.Combine(tempDir, "test_thumb.jpg");
            await File.WriteAllTextAsync(videoFile, "fake video content");
            await File.WriteAllTextAsync(thumbFile, "fake thumb content");

            var video = await AddVideoAsync(filePath: videoFile, thumbnailPath: thumbFile);

            var result = await _service.DeleteVideoAsync(video.Id, deleteFile: true, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Null(result.ErrorMessage);

            // Physical files should be deleted
            Assert.False(File.Exists(videoFile));
            Assert.False(File.Exists(thumbFile));

            // But the DB record should still exist (soft deleted)
            var existsInDb = await _context.VideoEntries
                .IgnoreQueryFilters()
                .AnyAsync(v => v.Id == video.Id);
            Assert.True(existsInDb);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region 6. Soft deleting a non-existent video returns DeleteResult(false, ...)

    [Fact]
    public async Task SoftDelete_NonExistentVideo_ReturnsFailure()
    {
        var result = await _service.DeleteVideoAsync(999, deleteFile: false, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 7. Batch soft delete marks all specified videos as deleted

    [Fact]
    public async Task BatchSoftDelete_MarksAllVideosAsDeleted()
    {
        var v1 = await AddVideoAsync("Video 1");
        var v2 = await AddVideoAsync("Video 2");
        var v3 = await AddVideoAsync("Video 3");

        var result = await _service.BatchDeleteAsync(
            new List<int> { v1.Id, v2.Id, v3.Id },
            deleteFiles: false, progress: null, CancellationToken.None);

        Assert.Equal(3, result.SuccessCount);
        Assert.Equal(0, result.FailCount);
        Assert.Empty(result.Errors);

        // All videos should still exist in DB (soft deleted) but not visible in regular queries
        var regularCount = await _context.VideoEntries.CountAsync();
        Assert.Equal(0, regularCount);

        var allCount = await _context.VideoEntries
            .IgnoreQueryFilters()
            .CountAsync(v => v.IsDeleted);
        Assert.Equal(3, allCount);

        // Verify each video has IsDeleted=true and DeletedAt set
        var deletedVideos = await _context.VideoEntries
            .IgnoreQueryFilters()
            .Where(v => new[] { v1.Id, v2.Id, v3.Id }.Contains(v.Id))
            .ToListAsync();

        Assert.All(deletedVideos, v =>
        {
            Assert.True(v.IsDeleted);
            Assert.NotNull(v.DeletedAt);
        });
    }

    #endregion
}
