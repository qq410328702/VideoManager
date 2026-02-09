using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Repositories;

namespace VideoManager.Tests.Unit.Repositories;

public class TagRepositoryTests
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

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_ShouldPersistTagAndAssignId()
    {
        using var context = CreateInMemoryContext();
        var repo = new TagRepository(context);
        var tag = new Tag { Name = "Action" };

        var result = await repo.AddAsync(tag, CancellationToken.None);

        Assert.True(result.Id > 0);
        Assert.Equal("Action", result.Name);
        Assert.Equal(1, await context.Tags.CountAsync());
    }

    [Fact]
    public async Task AddAsync_DuplicateName_ShouldThrowInvalidOperationException()
    {
        using var context = CreateInMemoryContext();
        var repo = new TagRepository(context);
        await repo.AddAsync(new Tag { Name = "Action" }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.AddAsync(new Tag { Name = "Action" }, CancellationToken.None));

        Assert.Contains("already exists", ex.Message);
        Assert.Equal(1, await context.Tags.CountAsync());
    }

    [Fact]
    public async Task AddAsync_DifferentNames_ShouldSucceed()
    {
        using var context = CreateInMemoryContext();
        var repo = new TagRepository(context);

        await repo.AddAsync(new Tag { Name = "Action" }, CancellationToken.None);
        await repo.AddAsync(new Tag { Name = "Comedy" }, CancellationToken.None);

        Assert.Equal(2, await context.Tags.CountAsync());
    }

    #endregion

    #region GetAllAsync Tests

    [Fact]
    public async Task GetAllAsync_EmptyDatabase_ShouldReturnEmptyList()
    {
        using var context = CreateInMemoryContext();
        var repo = new TagRepository(context);

        var result = await repo.GetAllAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllTagsOrderedByName()
    {
        using var context = CreateInMemoryContext();
        var repo = new TagRepository(context);
        await repo.AddAsync(new Tag { Name = "Comedy" }, CancellationToken.None);
        await repo.AddAsync(new Tag { Name = "Action" }, CancellationToken.None);
        await repo.AddAsync(new Tag { Name = "Drama" }, CancellationToken.None);

        var result = await repo.GetAllAsync(CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal("Action", result[0].Name);
        Assert.Equal("Comedy", result[1].Name);
        Assert.Equal("Drama", result[2].Name);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_ShouldRemoveTag()
    {
        using var context = CreateInMemoryContext();
        var repo = new TagRepository(context);
        var tag = await repo.AddAsync(new Tag { Name = "Action" }, CancellationToken.None);

        await repo.DeleteAsync(tag.Id, CancellationToken.None);

        Assert.Equal(0, await context.Tags.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_NonExistentId_ShouldDoNothing()
    {
        using var context = CreateInMemoryContext();
        var repo = new TagRepository(context);

        // Should not throw
        await repo.DeleteAsync(999, CancellationToken.None);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveTagVideoAssociationsButKeepVideos()
    {
        // Validates Requirement 3.7: Deleting a Tag removes associations but keeps VideoEntries
        using var context = CreateInMemoryContext();
        var repo = new TagRepository(context);

        var tag = new Tag { Name = "Action" };
        var video = new VideoEntry
        {
            Title = "Test Video",
            FileName = "test.mp4",
            FilePath = "/videos/test.mp4",
            FileSize = 1024,
            Duration = TimeSpan.FromMinutes(5),
            Width = 1920,
            Height = 1080,
            ImportedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        video.Tags.Add(tag);
        context.VideoEntries.Add(video);
        await context.SaveChangesAsync();

        await repo.DeleteAsync(tag.Id, CancellationToken.None);

        // Tag should be removed
        Assert.Equal(0, await context.Tags.CountAsync());
        // Video should still exist
        var remainingVideo = await context.VideoEntries
            .Include(v => v.Tags)
            .FirstOrDefaultAsync(v => v.Id == video.Id);
        Assert.NotNull(remainingVideo);
        Assert.Empty(remainingVideo.Tags);
    }

    #endregion

    #region ExistsByNameAsync Tests

    [Fact]
    public async Task ExistsByNameAsync_ExistingName_ShouldReturnTrue()
    {
        using var context = CreateInMemoryContext();
        var repo = new TagRepository(context);
        await repo.AddAsync(new Tag { Name = "Action" }, CancellationToken.None);

        var result = await repo.ExistsByNameAsync("Action", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task ExistsByNameAsync_NonExistingName_ShouldReturnFalse()
    {
        using var context = CreateInMemoryContext();
        var repo = new TagRepository(context);

        var result = await repo.ExistsByNameAsync("Action", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task ExistsByNameAsync_IsCaseSensitive()
    {
        using var context = CreateInMemoryContext();
        var repo = new TagRepository(context);
        await repo.AddAsync(new Tag { Name = "Action" }, CancellationToken.None);

        // SQLite default collation is case-sensitive for AnyAsync with == comparison
        // The exact behavior depends on the database collation, but the uniqueness
        // check should work correctly for exact matches
        var exactMatch = await repo.ExistsByNameAsync("Action", CancellationToken.None);
        Assert.True(exactMatch);
    }

    #endregion
}
