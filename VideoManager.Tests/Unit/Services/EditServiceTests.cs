using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

public class EditServiceTests : IDisposable
{
    private readonly VideoManagerDbContext _context;
    private readonly EditService _service;

    public EditServiceTests()
    {
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _context = new VideoManagerDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _service = new EditService(_context);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    private async Task<VideoEntry> AddVideoAsync(string title, string? description = null)
    {
        var video = new VideoEntry
        {
            Title = title,
            Description = description,
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

    #region UpdateVideoInfoAsync — Title Validation

    [Fact]
    public async Task UpdateVideoInfoAsync_NullTitle_ThrowsArgumentException()
    {
        var video = await AddVideoAsync("Original Title");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateVideoInfoAsync(video.Id, null!, "desc", CancellationToken.None));
    }

    [Fact]
    public async Task UpdateVideoInfoAsync_EmptyTitle_ThrowsArgumentException()
    {
        var video = await AddVideoAsync("Original Title");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateVideoInfoAsync(video.Id, "", "desc", CancellationToken.None));
    }

    [Fact]
    public async Task UpdateVideoInfoAsync_WhitespaceTitle_ThrowsArgumentException()
    {
        var video = await AddVideoAsync("Original Title");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateVideoInfoAsync(video.Id, "   ", "desc", CancellationToken.None));
    }

    [Fact]
    public async Task UpdateVideoInfoAsync_InvalidTitle_DoesNotChangeOriginalTitle()
    {
        var video = await AddVideoAsync("Original Title");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateVideoInfoAsync(video.Id, "", "new desc", CancellationToken.None));

        // Reload from database to verify title unchanged
        var reloaded = await _context.VideoEntries.FindAsync(video.Id);
        Assert.Equal("Original Title", reloaded!.Title);
    }

    #endregion

    #region UpdateVideoInfoAsync — Video Not Found

    [Fact]
    public async Task UpdateVideoInfoAsync_NonExistentVideo_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.UpdateVideoInfoAsync(999, "New Title", null, CancellationToken.None));
    }

    #endregion

    #region UpdateVideoInfoAsync — Successful Update

    [Fact]
    public async Task UpdateVideoInfoAsync_ValidInput_UpdatesTitleAndDescription()
    {
        var video = await AddVideoAsync("Old Title", "Old Description");

        var result = await _service.UpdateVideoInfoAsync(video.Id, "New Title", "New Description", CancellationToken.None);

        Assert.Equal("New Title", result.Title);
        Assert.Equal("New Description", result.Description);
    }

    [Fact]
    public async Task UpdateVideoInfoAsync_ValidInput_PersistsToDatabase()
    {
        var video = await AddVideoAsync("Old Title", "Old Description");

        await _service.UpdateVideoInfoAsync(video.Id, "New Title", "New Description", CancellationToken.None);

        // Reload from a fresh query to verify persistence
        var reloaded = await _context.VideoEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == video.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("New Title", reloaded.Title);
        Assert.Equal("New Description", reloaded.Description);
    }

    [Fact]
    public async Task UpdateVideoInfoAsync_NullDescription_SetsDescriptionToNull()
    {
        var video = await AddVideoAsync("Title", "Has Description");

        var result = await _service.UpdateVideoInfoAsync(video.Id, "Title", null, CancellationToken.None);

        Assert.Null(result.Description);

        var reloaded = await _context.VideoEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == video.Id);
        Assert.Null(reloaded!.Description);
    }

    [Fact]
    public async Task UpdateVideoInfoAsync_DoesNotAffectOtherFields()
    {
        var video = await AddVideoAsync("Title", "Desc");
        var originalFileName = video.FileName;
        var originalFilePath = video.FilePath;
        var originalFileSize = video.FileSize;

        await _service.UpdateVideoInfoAsync(video.Id, "Updated Title", "Updated Desc", CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == video.Id);
        Assert.Equal(originalFileName, reloaded!.FileName);
        Assert.Equal(originalFilePath, reloaded.FilePath);
        Assert.Equal(originalFileSize, reloaded.FileSize);
    }

    [Fact]
    public async Task UpdateVideoInfoAsync_ReturnsVideoWithTags()
    {
        var video = await AddVideoAsync("Title");
        var tag = await AddTagAsync("TestTag");
        video.Tags.Add(tag);
        await _context.SaveChangesAsync();

        var result = await _service.UpdateVideoInfoAsync(video.Id, "New Title", null, CancellationToken.None);

        Assert.Single(result.Tags);
        Assert.Equal("TestTag", result.Tags.First().Name);
    }

    #endregion

    #region AddTagToVideoAsync — Successful Add

    [Fact]
    public async Task AddTagToVideoAsync_ValidIds_AddsTagToVideo()
    {
        var video = await AddVideoAsync("Test Video");
        var tag = await AddTagAsync("Action");

        await _service.AddTagToVideoAsync(video.Id, tag.Id, CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .Include(v => v.Tags)
            .FirstOrDefaultAsync(v => v.Id == video.Id);
        Assert.Single(reloaded!.Tags);
        Assert.Equal("Action", reloaded.Tags.First().Name);
    }

    [Fact]
    public async Task AddTagToVideoAsync_MultipleTags_AddsAllTags()
    {
        var video = await AddVideoAsync("Test Video");
        var tag1 = await AddTagAsync("Action");
        var tag2 = await AddTagAsync("Comedy");
        var tag3 = await AddTagAsync("Drama");

        await _service.AddTagToVideoAsync(video.Id, tag1.Id, CancellationToken.None);
        await _service.AddTagToVideoAsync(video.Id, tag2.Id, CancellationToken.None);
        await _service.AddTagToVideoAsync(video.Id, tag3.Id, CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .Include(v => v.Tags)
            .FirstOrDefaultAsync(v => v.Id == video.Id);
        Assert.Equal(3, reloaded!.Tags.Count);
    }

    [Fact]
    public async Task AddTagToVideoAsync_DuplicateTag_DoesNotAddTwice()
    {
        var video = await AddVideoAsync("Test Video");
        var tag = await AddTagAsync("Action");

        await _service.AddTagToVideoAsync(video.Id, tag.Id, CancellationToken.None);
        await _service.AddTagToVideoAsync(video.Id, tag.Id, CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .Include(v => v.Tags)
            .FirstOrDefaultAsync(v => v.Id == video.Id);
        Assert.Single(reloaded!.Tags);
    }

    #endregion

    #region AddTagToVideoAsync — Not Found

    [Fact]
    public async Task AddTagToVideoAsync_NonExistentVideo_ThrowsKeyNotFoundException()
    {
        var tag = await AddTagAsync("Action");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.AddTagToVideoAsync(999, tag.Id, CancellationToken.None));
    }

    [Fact]
    public async Task AddTagToVideoAsync_NonExistentTag_ThrowsKeyNotFoundException()
    {
        var video = await AddVideoAsync("Test Video");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.AddTagToVideoAsync(video.Id, 999, CancellationToken.None));
    }

    #endregion

    #region RemoveTagFromVideoAsync — Successful Remove

    [Fact]
    public async Task RemoveTagFromVideoAsync_ExistingAssociation_RemovesTag()
    {
        var video = await AddVideoAsync("Test Video");
        var tag = await AddTagAsync("Action");
        video.Tags.Add(tag);
        await _context.SaveChangesAsync();

        await _service.RemoveTagFromVideoAsync(video.Id, tag.Id, CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .Include(v => v.Tags)
            .FirstOrDefaultAsync(v => v.Id == video.Id);
        Assert.Empty(reloaded!.Tags);
    }

    [Fact]
    public async Task RemoveTagFromVideoAsync_PreservesOtherTags()
    {
        var video = await AddVideoAsync("Test Video");
        var tag1 = await AddTagAsync("Action");
        var tag2 = await AddTagAsync("Comedy");
        video.Tags.Add(tag1);
        video.Tags.Add(tag2);
        await _context.SaveChangesAsync();

        await _service.RemoveTagFromVideoAsync(video.Id, tag1.Id, CancellationToken.None);

        var reloaded = await _context.VideoEntries
            .Include(v => v.Tags)
            .FirstOrDefaultAsync(v => v.Id == video.Id);
        Assert.Single(reloaded!.Tags);
        Assert.Equal("Comedy", reloaded.Tags.First().Name);
    }

    [Fact]
    public async Task RemoveTagFromVideoAsync_PreservesTagEntity()
    {
        var video = await AddVideoAsync("Test Video");
        var tag = await AddTagAsync("Action");
        video.Tags.Add(tag);
        await _context.SaveChangesAsync();

        await _service.RemoveTagFromVideoAsync(video.Id, tag.Id, CancellationToken.None);

        // Tag itself should still exist in the database
        var tagExists = await _context.Tags.AnyAsync(t => t.Id == tag.Id);
        Assert.True(tagExists);
    }

    [Fact]
    public async Task RemoveTagFromVideoAsync_PreservesVideoEntry()
    {
        var video = await AddVideoAsync("Test Video");
        var tag = await AddTagAsync("Action");
        video.Tags.Add(tag);
        await _context.SaveChangesAsync();

        await _service.RemoveTagFromVideoAsync(video.Id, tag.Id, CancellationToken.None);

        // Video should still exist
        var videoExists = await _context.VideoEntries.AnyAsync(v => v.Id == video.Id);
        Assert.True(videoExists);
    }

    #endregion

    #region RemoveTagFromVideoAsync — Not Found

    [Fact]
    public async Task RemoveTagFromVideoAsync_NonExistentVideo_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.RemoveTagFromVideoAsync(999, 1, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveTagFromVideoAsync_TagNotOnVideo_ThrowsKeyNotFoundException()
    {
        var video = await AddVideoAsync("Test Video");
        var tag = await AddTagAsync("Action");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.RemoveTagFromVideoAsync(video.Id, tag.Id, CancellationToken.None));
    }

    #endregion
}
