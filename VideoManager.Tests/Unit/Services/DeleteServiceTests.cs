using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

public class DeleteServiceTests : IDisposable
{
    private readonly VideoManagerDbContext _context;
    private readonly DeleteService _service;

    public DeleteServiceTests()
    {
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _context = new VideoManagerDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _service = new DeleteService(_context, NullLogger<DeleteService>.Instance);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

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

    #region DeleteVideoAsync — Remove from library only (deleteFile=false)

    [Fact]
    public async Task DeleteVideoAsync_RemoveFromLibrary_DeletesDbRecord()
    {
        var video = await AddVideoAsync();

        var result = await _service.DeleteVideoAsync(video.Id, deleteFile: false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        var exists = await _context.VideoEntries.AnyAsync(v => v.Id == video.Id);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteVideoAsync_RemoveFromLibrary_ClearsTagAssociations()
    {
        var video = await AddVideoAsync();
        var tag = await AddTagAsync("Action");
        video.Tags.Add(tag);
        await _context.SaveChangesAsync();

        await _service.DeleteVideoAsync(video.Id, deleteFile: false, CancellationToken.None);

        // Video should be gone
        var videoExists = await _context.VideoEntries.AnyAsync(v => v.Id == video.Id);
        Assert.False(videoExists);

        // Tag itself should still exist
        var tagExists = await _context.Tags.AnyAsync(t => t.Id == tag.Id);
        Assert.True(tagExists);
    }

    [Fact]
    public async Task DeleteVideoAsync_RemoveFromLibrary_ClearsCategoryAssociations()
    {
        var video = await AddVideoAsync();
        var category = await AddCategoryAsync("Movies");
        video.Categories.Add(category);
        await _context.SaveChangesAsync();

        await _service.DeleteVideoAsync(video.Id, deleteFile: false, CancellationToken.None);

        // Video should be gone
        var videoExists = await _context.VideoEntries.AnyAsync(v => v.Id == video.Id);
        Assert.False(videoExists);

        // Category itself should still exist
        var categoryExists = await _context.FolderCategories.AnyAsync(c => c.Id == category.Id);
        Assert.True(categoryExists);
    }

    #endregion

    #region DeleteVideoAsync — Not Found

    [Fact]
    public async Task DeleteVideoAsync_NonExistentVideo_ReturnsFailure()
    {
        var result = await _service.DeleteVideoAsync(999, deleteFile: false, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not found", result.ErrorMessage);
    }

    #endregion

    #region DeleteVideoAsync — Delete with files (deleteFile=true)

    [Fact]
    public async Task DeleteVideoAsync_DeleteWithFiles_DeletesDbRecordWhenFileDoesNotExist()
    {
        // File path points to non-existent file — DB deletion should still succeed (Req 12.4)
        var video = await AddVideoAsync(filePath: "/nonexistent/video.mp4", thumbnailPath: "/nonexistent/thumb.jpg");

        var result = await _service.DeleteVideoAsync(video.Id, deleteFile: true, CancellationToken.None);

        Assert.True(result.Success);
        // No error because File.Exists returns false, so no deletion is attempted
        Assert.Null(result.ErrorMessage);
        var exists = await _context.VideoEntries.AnyAsync(v => v.Id == video.Id);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteVideoAsync_DeleteWithFiles_DeletesActualFiles()
    {
        // Create temporary files to verify they get deleted
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
            Assert.False(File.Exists(videoFile));
            Assert.False(File.Exists(thumbFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task DeleteVideoAsync_DeleteWithFiles_KeepsFilesWhenDeleteFileFalse()
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

            var result = await _service.DeleteVideoAsync(video.Id, deleteFile: false, CancellationToken.None);

            Assert.True(result.Success);
            // Files should still exist
            Assert.True(File.Exists(videoFile));
            Assert.True(File.Exists(thumbFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region BatchDeleteAsync

    [Fact]
    public async Task BatchDeleteAsync_AllSucceed_ReturnsCorrectCounts()
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
    }

    [Fact]
    public async Task BatchDeleteAsync_SomeNotFound_ReportsFailures()
    {
        var v1 = await AddVideoAsync("Video 1");

        var result = await _service.BatchDeleteAsync(
            new List<int> { v1.Id, 998, 999 },
            deleteFiles: false, progress: null, CancellationToken.None);

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(2, result.FailCount);
        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, e => Assert.Contains("not found", e.Reason));
    }

    [Fact]
    public async Task BatchDeleteAsync_ReportsProgress()
    {
        var v1 = await AddVideoAsync("Video 1");
        var v2 = await AddVideoAsync("Video 2");

        var progressReports = new List<BatchProgress>();
        var progress = new Progress<BatchProgress>(p => progressReports.Add(p));

        await _service.BatchDeleteAsync(
            new List<int> { v1.Id, v2.Id },
            deleteFiles: false, progress: progress, CancellationToken.None);

        // Progress may be reported asynchronously, so give it a moment
        await Task.Delay(100);

        Assert.Equal(2, progressReports.Count);
        Assert.Equal(1, progressReports[0].Completed);
        Assert.Equal(2, progressReports[0].Total);
        Assert.Equal(2, progressReports[1].Completed);
        Assert.Equal(2, progressReports[1].Total);
    }

    [Fact]
    public async Task BatchDeleteAsync_EmptyList_ReturnsZeroCounts()
    {
        var result = await _service.BatchDeleteAsync(
            new List<int>(), deleteFiles: false, progress: null, CancellationToken.None);

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(0, result.FailCount);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task BatchDeleteAsync_CancellationRespected()
    {
        var v1 = await AddVideoAsync("Video 1");
        var v2 = await AddVideoAsync("Video 2");
        var v3 = await AddVideoAsync("Video 3");

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.BatchDeleteAsync(
                new List<int> { v1.Id, v2.Id, v3.Id },
                deleteFiles: false, progress: null, cts.Token));
    }

    #endregion
}
