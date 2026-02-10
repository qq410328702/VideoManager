using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Repositories;

namespace VideoManager.Tests.Unit.Repositories;

public class VideoRepositoryTests
{
    private static VideoManagerDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var context = new VideoManagerDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    private static VideoEntry CreateSampleEntry(string title = "Test Video", string fileName = "test.mp4")
    {
        return new VideoEntry
        {
            Title = title,
            FileName = fileName,
            FilePath = $"/videos/{fileName}",
            FileSize = 1024 * 1024,
            Duration = TimeSpan.FromMinutes(5),
            Width = 1920,
            Height = 1080,
            ImportedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task AddAsync_ShouldPersistEntryAndAssignId()
    {
        using var context = CreateInMemoryContext();
        var repo = new VideoRepository(context, NullLogger<VideoRepository>.Instance);
        var entry = CreateSampleEntry();

        var result = await repo.AddAsync(entry, CancellationToken.None);

        Assert.True(result.Id > 0);
        Assert.Equal("Test Video", result.Title);
        Assert.Equal(1, await context.VideoEntries.CountAsync());
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnEntryWithTagsAndCategories()
    {
        using var context = CreateInMemoryContext();
        var repo = new VideoRepository(context, NullLogger<VideoRepository>.Instance);
        var entry = CreateSampleEntry();
        entry.Tags.Add(new Tag { Name = "Action" });
        entry.Categories.Add(new FolderCategory { Name = "Movies" });
        await repo.AddAsync(entry, CancellationToken.None);

        var result = await repo.GetByIdAsync(entry.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Test Video", result.Title);
        Assert.Single(result.Tags);
        Assert.Single(result.Categories);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNullForNonExistentId()
    {
        using var context = CreateInMemoryContext();
        var repo = new VideoRepository(context, NullLogger<VideoRepository>.Instance);

        var result = await repo.GetByIdAsync(999, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistChanges()
    {
        using var context = CreateInMemoryContext();
        var repo = new VideoRepository(context, NullLogger<VideoRepository>.Instance);
        var entry = CreateSampleEntry();
        await repo.AddAsync(entry, CancellationToken.None);

        entry.Title = "Updated Title";
        entry.Description = "New description";
        await repo.UpdateAsync(entry, CancellationToken.None);

        var result = await repo.GetByIdAsync(entry.Id, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("Updated Title", result.Title);
        Assert.Equal("New description", result.Description);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveEntry()
    {
        using var context = CreateInMemoryContext();
        var repo = new VideoRepository(context, NullLogger<VideoRepository>.Instance);
        var entry = CreateSampleEntry();
        await repo.AddAsync(entry, CancellationToken.None);

        await repo.DeleteAsync(entry.Id, CancellationToken.None);

        var result = await repo.GetByIdAsync(entry.Id, CancellationToken.None);
        Assert.Null(result);
        Assert.Equal(0, await context.VideoEntries.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_ShouldDoNothingForNonExistentId()
    {
        using var context = CreateInMemoryContext();
        var repo = new VideoRepository(context, NullLogger<VideoRepository>.Instance);

        // Should not throw
        await repo.DeleteAsync(999, CancellationToken.None);
    }

    [Fact]
    public async Task GetPagedAsync_ShouldReturnCorrectPage()
    {
        using var context = CreateInMemoryContext();
        var repo = new VideoRepository(context, NullLogger<VideoRepository>.Instance);

        // Add 5 entries with distinct ImportedAt so default descending sort is deterministic
        var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 1; i <= 5; i++)
        {
            var entry = CreateSampleEntry($"Video {i}", $"video{i}.mp4");
            entry.ImportedAt = baseDate.AddDays(i); // Video 1 = Jan 2, ..., Video 5 = Jan 6
            await repo.AddAsync(entry, CancellationToken.None);
        }

        // Default sort is ImportedAt Descending, so order is: Video 5, 4, 3, 2, 1
        // Page 2 with pageSize 2 should return Video 3, Video 2
        var result = await repo.GetPagedAsync(page: 2, pageSize: 2, CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Video 3", result.Items[0].Title);
        Assert.Equal("Video 2", result.Items[1].Title);
    }

    [Fact]
    public async Task GetPagedAsync_LastPageShouldReturnRemainingItems()
    {
        using var context = CreateInMemoryContext();
        var repo = new VideoRepository(context, NullLogger<VideoRepository>.Instance);

        var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 1; i <= 5; i++)
        {
            var entry = CreateSampleEntry($"Video {i}", $"video{i}.mp4");
            entry.ImportedAt = baseDate.AddDays(i); // Video 1 = Jan 2, ..., Video 5 = Jan 6
            await repo.AddAsync(entry, CancellationToken.None);
        }

        // Default sort is ImportedAt Descending, so order is: Video 5, 4, 3, 2, 1
        // Page 3 with pageSize 2 should return the last item: Video 1
        var result = await repo.GetPagedAsync(page: 3, pageSize: 2, CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Single(result.Items);
        Assert.Equal("Video 1", result.Items[0].Title);
    }

    [Fact]
    public async Task GetPagedAsync_EmptyDatabase_ShouldReturnEmptyResult()
    {
        using var context = CreateInMemoryContext();
        var repo = new VideoRepository(context, NullLogger<VideoRepository>.Instance);

        var result = await repo.GetPagedAsync(page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
    }

    [Fact]
    public async Task GetPagedAsync_PageBeyondData_ShouldReturnEmptyItems()
    {
        using var context = CreateInMemoryContext();
        var repo = new VideoRepository(context, NullLogger<VideoRepository>.Instance);

        await repo.AddAsync(CreateSampleEntry(), CancellationToken.None);

        var result = await repo.GetPagedAsync(page: 5, pageSize: 10, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Empty(result.Items);
    }
}
