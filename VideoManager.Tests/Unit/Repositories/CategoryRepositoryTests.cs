using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Repositories;

namespace VideoManager.Tests.Unit.Repositories;

public class CategoryRepositoryTests
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

    private static VideoEntry CreateSampleVideo(string title = "Test Video", string fileName = "test.mp4")
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

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_ShouldPersistCategoryAndAssignId()
    {
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);
        var category = new FolderCategory { Name = "Movies" };

        var result = await repo.AddAsync(category, CancellationToken.None);

        Assert.True(result.Id > 0);
        Assert.Equal("Movies", result.Name);
        Assert.Equal(1, await context.FolderCategories.CountAsync());
    }

    [Fact]
    public async Task AddAsync_WithParentId_ShouldSetParentRelationship()
    {
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);

        var parent = await repo.AddAsync(new FolderCategory { Name = "Movies" }, CancellationToken.None);
        var child = await repo.AddAsync(
            new FolderCategory { Name = "Action", ParentId = parent.Id },
            CancellationToken.None);

        Assert.Equal(parent.Id, child.ParentId);
        Assert.Equal(2, await context.FolderCategories.CountAsync());
    }

    [Fact]
    public async Task AddAsync_MultipleCategoriesWithSameName_ShouldSucceed()
    {
        // FolderCategory names are not required to be unique (unlike Tags)
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);

        await repo.AddAsync(new FolderCategory { Name = "Favorites" }, CancellationToken.None);
        await repo.AddAsync(new FolderCategory { Name = "Favorites" }, CancellationToken.None);

        Assert.Equal(2, await context.FolderCategories.CountAsync());
    }

    #endregion

    #region GetTreeAsync Tests

    [Fact]
    public async Task GetTreeAsync_EmptyDatabase_ShouldReturnEmptyList()
    {
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);

        var result = await repo.GetTreeAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTreeAsync_SingleRootCategory_ShouldReturnOneRoot()
    {
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);
        await repo.AddAsync(new FolderCategory { Name = "Movies" }, CancellationToken.None);

        var result = await repo.GetTreeAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Movies", result[0].Name);
        Assert.Null(result[0].ParentId);
        Assert.Empty(result[0].Children);
    }

    [Fact]
    public async Task GetTreeAsync_ShouldReturnOnlyRootNodes()
    {
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);

        var root = await repo.AddAsync(new FolderCategory { Name = "Movies" }, CancellationToken.None);
        await repo.AddAsync(new FolderCategory { Name = "Action", ParentId = root.Id }, CancellationToken.None);
        await repo.AddAsync(new FolderCategory { Name = "Comedy", ParentId = root.Id }, CancellationToken.None);

        var result = await repo.GetTreeAsync(CancellationToken.None);

        // Only root nodes should be returned at the top level
        Assert.Single(result);
        Assert.Equal("Movies", result[0].Name);
    }

    [Fact]
    public async Task GetTreeAsync_ShouldLoadChildrenCorrectly()
    {
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);

        var root = await repo.AddAsync(new FolderCategory { Name = "Movies" }, CancellationToken.None);
        await repo.AddAsync(new FolderCategory { Name = "Action", ParentId = root.Id }, CancellationToken.None);
        await repo.AddAsync(new FolderCategory { Name = "Comedy", ParentId = root.Id }, CancellationToken.None);

        var result = await repo.GetTreeAsync(CancellationToken.None);

        Assert.Equal(2, result[0].Children.Count);
        var childNames = result[0].Children.Select(c => c.Name).OrderBy(n => n).ToList();
        Assert.Equal("Action", childNames[0]);
        Assert.Equal("Comedy", childNames[1]);
    }

    [Fact]
    public async Task GetTreeAsync_MultiLevelNesting_ShouldReturnFullTree()
    {
        // Validates Requirement 3.4: Support multi-level nested tree structure
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);

        var root = await repo.AddAsync(new FolderCategory { Name = "Videos" }, CancellationToken.None);
        var level1 = await repo.AddAsync(
            new FolderCategory { Name = "Movies", ParentId = root.Id }, CancellationToken.None);
        var level2 = await repo.AddAsync(
            new FolderCategory { Name = "Action", ParentId = level1.Id }, CancellationToken.None);
        await repo.AddAsync(
            new FolderCategory { Name = "Marvel", ParentId = level2.Id }, CancellationToken.None);

        var result = await repo.GetTreeAsync(CancellationToken.None);

        // Verify 4-level deep tree: Videos -> Movies -> Action -> Marvel
        Assert.Single(result);
        Assert.Equal("Videos", result[0].Name);

        var movies = result[0].Children.Single();
        Assert.Equal("Movies", movies.Name);

        var action = movies.Children.Single();
        Assert.Equal("Action", action.Name);

        var marvel = action.Children.Single();
        Assert.Equal("Marvel", marvel.Name);
        Assert.Empty(marvel.Children);
    }

    [Fact]
    public async Task GetTreeAsync_MultipleRoots_ShouldReturnAllRoots()
    {
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);

        await repo.AddAsync(new FolderCategory { Name = "Movies" }, CancellationToken.None);
        await repo.AddAsync(new FolderCategory { Name = "TV Shows" }, CancellationToken.None);
        await repo.AddAsync(new FolderCategory { Name = "Documentaries" }, CancellationToken.None);

        var result = await repo.GetTreeAsync(CancellationToken.None);

        Assert.Equal(3, result.Count);
        var names = result.Select(c => c.Name).OrderBy(n => n).ToList();
        Assert.Equal("Documentaries", names[0]);
        Assert.Equal("Movies", names[1]);
        Assert.Equal("TV Shows", names[2]);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_ShouldRemoveCategory()
    {
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);
        var category = await repo.AddAsync(new FolderCategory { Name = "Movies" }, CancellationToken.None);

        await repo.DeleteAsync(category.Id, CancellationToken.None);

        Assert.Equal(0, await context.FolderCategories.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_NonExistentId_ShouldDoNothing()
    {
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);

        // Should not throw
        await repo.DeleteAsync(999, CancellationToken.None);
    }

    [Fact]
    public async Task DeleteAsync_ShouldCascadeDeleteChildren()
    {
        // Cascade delete is configured in DbContext for parent-child relationship
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);

        var parent = await repo.AddAsync(new FolderCategory { Name = "Movies" }, CancellationToken.None);
        await repo.AddAsync(new FolderCategory { Name = "Action", ParentId = parent.Id }, CancellationToken.None);
        await repo.AddAsync(new FolderCategory { Name = "Comedy", ParentId = parent.Id }, CancellationToken.None);

        await repo.DeleteAsync(parent.Id, CancellationToken.None);

        Assert.Equal(0, await context.FolderCategories.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveVideoAssociationsButKeepVideos()
    {
        // Validates Requirement 3.6: Deleting a FolderCategory removes associations but keeps VideoEntries
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);

        var category = new FolderCategory { Name = "Movies" };
        var video = CreateSampleVideo();
        video.Categories.Add(category);
        context.VideoEntries.Add(video);
        await context.SaveChangesAsync();

        await repo.DeleteAsync(category.Id, CancellationToken.None);

        // Category should be removed
        Assert.Equal(0, await context.FolderCategories.CountAsync());
        // Video should still exist
        var remainingVideo = await context.VideoEntries
            .Include(v => v.Categories)
            .FirstOrDefaultAsync(v => v.Id == video.Id);
        Assert.NotNull(remainingVideo);
        Assert.Empty(remainingVideo.Categories);
    }

    [Fact]
    public async Task DeleteAsync_ShouldNotAffectOtherCategories()
    {
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);

        var cat1 = await repo.AddAsync(new FolderCategory { Name = "Movies" }, CancellationToken.None);
        var cat2 = await repo.AddAsync(new FolderCategory { Name = "TV Shows" }, CancellationToken.None);

        await repo.DeleteAsync(cat1.Id, CancellationToken.None);

        Assert.Equal(1, await context.FolderCategories.CountAsync());
        var remaining = await context.FolderCategories.FirstAsync();
        Assert.Equal("TV Shows", remaining.Name);
    }

    [Fact]
    public async Task DeleteAsync_ChildCategory_ShouldNotAffectParent()
    {
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);

        var parent = await repo.AddAsync(new FolderCategory { Name = "Movies" }, CancellationToken.None);
        var child = await repo.AddAsync(
            new FolderCategory { Name = "Action", ParentId = parent.Id }, CancellationToken.None);

        await repo.DeleteAsync(child.Id, CancellationToken.None);

        Assert.Equal(1, await context.FolderCategories.CountAsync());
        var remaining = await context.FolderCategories.FirstAsync();
        Assert.Equal("Movies", remaining.Name);
    }

    [Fact]
    public async Task DeleteAsync_CategoryWithMultipleVideos_ShouldKeepAllVideos()
    {
        // Validates Requirement 3.6: All associated videos remain intact
        using var context = CreateInMemoryContext();
        var repo = new CategoryRepository(context);

        var category = new FolderCategory { Name = "Movies" };
        var video1 = CreateSampleVideo("Video 1", "video1.mp4");
        var video2 = CreateSampleVideo("Video 2", "video2.mp4");
        video1.Categories.Add(category);
        video2.Categories.Add(category);
        context.VideoEntries.Add(video1);
        context.VideoEntries.Add(video2);
        await context.SaveChangesAsync();

        await repo.DeleteAsync(category.Id, CancellationToken.None);

        // Both videos should still exist
        Assert.Equal(2, await context.VideoEntries.CountAsync());
        Assert.Equal(0, await context.FolderCategories.CountAsync());
    }

    #endregion
}
