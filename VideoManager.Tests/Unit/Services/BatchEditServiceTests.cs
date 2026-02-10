using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

public class BatchEditServiceTests : IDisposable
{
    private readonly VideoManagerDbContext _context;
    private readonly EditService _service;

    public BatchEditServiceTests()
    {
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _context = new VideoManagerDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _service = new EditService(_context, NullLogger<EditService>.Instance);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    private async Task<VideoEntry> AddVideoAsync(string title)
    {
        var video = new VideoEntry
        {
            Title = title,
            FileName = $"{title.Replace(" ", "_").ToLower()}.mp4",
            FilePath = $"/videos/{title.Replace(" ", "_").ToLower()}.mp4",
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

    #region BatchAddTagAsync — Validation

    [Fact]
    public async Task BatchAddTagAsync_NullVideoIds_ThrowsArgumentException()
    {
        var tag = await AddTagAsync("Action");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.BatchAddTagAsync(null!, tag.Id, CancellationToken.None));
    }

    [Fact]
    public async Task BatchAddTagAsync_EmptyVideoIds_ThrowsArgumentException()
    {
        var tag = await AddTagAsync("Action");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.BatchAddTagAsync(new List<int>(), tag.Id, CancellationToken.None));
    }

    [Fact]
    public async Task BatchAddTagAsync_NonExistentTag_ThrowsKeyNotFoundException()
    {
        var video = await AddVideoAsync("Test Video");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.BatchAddTagAsync(new List<int> { video.Id }, 999, CancellationToken.None));
    }

    #endregion

    #region BatchAddTagAsync — Successful Operations

    [Fact]
    public async Task BatchAddTagAsync_SingleVideo_AddsTagToVideo()
    {
        var video = await AddVideoAsync("Video 1");
        var tag = await AddTagAsync("Action");

        await _service.BatchAddTagAsync(new List<int> { video.Id }, tag.Id, CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .Include(v => v.Tags)
            .FirstAsync(v => v.Id == video.Id);
        Assert.Single(reloaded.Tags);
        Assert.Equal("Action", reloaded.Tags.First().Name);
    }

    [Fact]
    public async Task BatchAddTagAsync_MultipleVideos_AddsTagToAllVideos()
    {
        var video1 = await AddVideoAsync("Video 1");
        var video2 = await AddVideoAsync("Video 2");
        var video3 = await AddVideoAsync("Video 3");
        var tag = await AddTagAsync("Action");

        await _service.BatchAddTagAsync(
            new List<int> { video1.Id, video2.Id, video3.Id },
            tag.Id,
            CancellationToken.None);

        foreach (var videoId in new[] { video1.Id, video2.Id, video3.Id })
        {
            var reloaded = await _context.VideoEntries
                .Include(v => v.Tags)
                .FirstAsync(v => v.Id == videoId);
            Assert.Single(reloaded.Tags);
            Assert.Equal(tag.Id, reloaded.Tags.First().Id);
        }
    }

    [Fact]
    public async Task BatchAddTagAsync_VideoAlreadyHasTag_DoesNotDuplicate()
    {
        var video = await AddVideoAsync("Video 1");
        var tag = await AddTagAsync("Action");

        // Add tag first time
        await _service.BatchAddTagAsync(new List<int> { video.Id }, tag.Id, CancellationToken.None);
        // Add tag second time (should be idempotent)
        await _service.BatchAddTagAsync(new List<int> { video.Id }, tag.Id, CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .Include(v => v.Tags)
            .FirstAsync(v => v.Id == video.Id);
        Assert.Single(reloaded.Tags);
    }

    [Fact]
    public async Task BatchAddTagAsync_MixedExistingAndNew_OnlyAddsToVideosWithoutTag()
    {
        var video1 = await AddVideoAsync("Video 1");
        var video2 = await AddVideoAsync("Video 2");
        var tag = await AddTagAsync("Action");

        // Pre-add tag to video1
        video1.Tags.Add(tag);
        await _context.SaveChangesAsync();

        await _service.BatchAddTagAsync(
            new List<int> { video1.Id, video2.Id },
            tag.Id,
            CancellationToken.None);

        var reloaded1 = await _context.VideoEntries
            .Include(v => v.Tags)
            .FirstAsync(v => v.Id == video1.Id);
        var reloaded2 = await _context.VideoEntries
            .Include(v => v.Tags)
            .FirstAsync(v => v.Id == video2.Id);

        Assert.Single(reloaded1.Tags); // Still just one tag, not duplicated
        Assert.Single(reloaded2.Tags); // Tag was added
    }

    [Fact]
    public async Task BatchAddTagAsync_NonExistentVideoIds_SkipsGracefully()
    {
        var video = await AddVideoAsync("Video 1");
        var tag = await AddTagAsync("Action");

        // Include a non-existent video ID - should not throw, just skip
        await _service.BatchAddTagAsync(
            new List<int> { video.Id, 999 },
            tag.Id,
            CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .Include(v => v.Tags)
            .FirstAsync(v => v.Id == video.Id);
        Assert.Single(reloaded.Tags);
    }

    [Fact]
    public async Task BatchAddTagAsync_PreservesExistingTags()
    {
        var video = await AddVideoAsync("Video 1");
        var existingTag = await AddTagAsync("Comedy");
        var newTag = await AddTagAsync("Action");

        video.Tags.Add(existingTag);
        await _context.SaveChangesAsync();

        await _service.BatchAddTagAsync(new List<int> { video.Id }, newTag.Id, CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .Include(v => v.Tags)
            .FirstAsync(v => v.Id == video.Id);
        Assert.Equal(2, reloaded.Tags.Count);
        Assert.Contains(reloaded.Tags, t => t.Name == "Comedy");
        Assert.Contains(reloaded.Tags, t => t.Name == "Action");
    }

    #endregion

    #region BatchMoveToCategoryAsync — Validation

    [Fact]
    public async Task BatchMoveToCategoryAsync_NullVideoIds_ThrowsArgumentException()
    {
        var category = await AddCategoryAsync("Movies");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.BatchMoveToCategoryAsync(null!, category.Id, CancellationToken.None));
    }

    [Fact]
    public async Task BatchMoveToCategoryAsync_EmptyVideoIds_ThrowsArgumentException()
    {
        var category = await AddCategoryAsync("Movies");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.BatchMoveToCategoryAsync(new List<int>(), category.Id, CancellationToken.None));
    }

    [Fact]
    public async Task BatchMoveToCategoryAsync_NonExistentCategory_ThrowsKeyNotFoundException()
    {
        var video = await AddVideoAsync("Test Video");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.BatchMoveToCategoryAsync(new List<int> { video.Id }, 999, CancellationToken.None));
    }

    #endregion

    #region BatchMoveToCategoryAsync — Successful Operations

    [Fact]
    public async Task BatchMoveToCategoryAsync_SingleVideo_AddsToCategoryAsync()
    {
        var video = await AddVideoAsync("Video 1");
        var category = await AddCategoryAsync("Movies");

        await _service.BatchMoveToCategoryAsync(new List<int> { video.Id }, category.Id, CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .Include(v => v.Categories)
            .FirstAsync(v => v.Id == video.Id);
        Assert.Single(reloaded.Categories);
        Assert.Equal("Movies", reloaded.Categories.First().Name);
    }

    [Fact]
    public async Task BatchMoveToCategoryAsync_MultipleVideos_AddsAllToCategory()
    {
        var video1 = await AddVideoAsync("Video 1");
        var video2 = await AddVideoAsync("Video 2");
        var video3 = await AddVideoAsync("Video 3");
        var category = await AddCategoryAsync("Movies");

        await _service.BatchMoveToCategoryAsync(
            new List<int> { video1.Id, video2.Id, video3.Id },
            category.Id,
            CancellationToken.None);

        foreach (var videoId in new[] { video1.Id, video2.Id, video3.Id })
        {
            var reloaded = await _context.VideoEntries
                .Include(v => v.Categories)
                .FirstAsync(v => v.Id == videoId);
            Assert.Single(reloaded.Categories);
            Assert.Equal(category.Id, reloaded.Categories.First().Id);
        }
    }

    [Fact]
    public async Task BatchMoveToCategoryAsync_VideoAlreadyInCategory_DoesNotDuplicate()
    {
        var video = await AddVideoAsync("Video 1");
        var category = await AddCategoryAsync("Movies");

        // Move first time
        await _service.BatchMoveToCategoryAsync(new List<int> { video.Id }, category.Id, CancellationToken.None);
        // Move second time (should be idempotent)
        await _service.BatchMoveToCategoryAsync(new List<int> { video.Id }, category.Id, CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .Include(v => v.Categories)
            .FirstAsync(v => v.Id == video.Id);
        Assert.Single(reloaded.Categories);
    }

    [Fact]
    public async Task BatchMoveToCategoryAsync_PreservesExistingCategories()
    {
        var video = await AddVideoAsync("Video 1");
        var existingCategory = await AddCategoryAsync("Favorites");
        var newCategory = await AddCategoryAsync("Movies");

        video.Categories.Add(existingCategory);
        await _context.SaveChangesAsync();

        await _service.BatchMoveToCategoryAsync(new List<int> { video.Id }, newCategory.Id, CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .Include(v => v.Categories)
            .FirstAsync(v => v.Id == video.Id);
        Assert.Equal(2, reloaded.Categories.Count);
        Assert.Contains(reloaded.Categories, c => c.Name == "Favorites");
        Assert.Contains(reloaded.Categories, c => c.Name == "Movies");
    }

    [Fact]
    public async Task BatchMoveToCategoryAsync_NonExistentVideoIds_SkipsGracefully()
    {
        var video = await AddVideoAsync("Video 1");
        var category = await AddCategoryAsync("Movies");

        // Include a non-existent video ID - should not throw, just skip
        await _service.BatchMoveToCategoryAsync(
            new List<int> { video.Id, 999 },
            category.Id,
            CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .Include(v => v.Categories)
            .FirstAsync(v => v.Id == video.Id);
        Assert.Single(reloaded.Categories);
    }

    [Fact]
    public async Task BatchMoveToCategoryAsync_MixedExistingAndNew_OnlyAddsToVideosWithoutCategory()
    {
        var video1 = await AddVideoAsync("Video 1");
        var video2 = await AddVideoAsync("Video 2");
        var category = await AddCategoryAsync("Movies");

        // Pre-add category to video1
        video1.Categories.Add(category);
        await _context.SaveChangesAsync();

        await _service.BatchMoveToCategoryAsync(
            new List<int> { video1.Id, video2.Id },
            category.Id,
            CancellationToken.None);

        var reloaded1 = await _context.VideoEntries
            .Include(v => v.Categories)
            .FirstAsync(v => v.Id == video1.Id);
        var reloaded2 = await _context.VideoEntries
            .Include(v => v.Categories)
            .FirstAsync(v => v.Id == video2.Id);

        Assert.Single(reloaded1.Categories); // Still just one category, not duplicated
        Assert.Single(reloaded2.Categories); // Category was added
    }

    #endregion
}
