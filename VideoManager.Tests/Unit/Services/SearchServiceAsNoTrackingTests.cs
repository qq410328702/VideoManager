using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Verifies that SearchService.SearchAsync with AsNoTracking returns correct results
/// and includes Tags/Categories navigation properties.
/// </summary>
public class SearchServiceAsNoTrackingTests : IDisposable
{
    private readonly VideoManagerDbContext _context;
    private readonly SearchService _service;

    public SearchServiceAsNoTrackingTests()
    {
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _context = new VideoManagerDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _service = new SearchService(_context, NullLogger<SearchService>.Instance);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    private VideoEntry CreateVideo(
        string title,
        string? description = null,
        TimeSpan? duration = null,
        DateTime? importedAt = null,
        string? fileName = null)
    {
        return new VideoEntry
        {
            Title = title,
            Description = description,
            FileName = fileName ?? $"{title.Replace(" ", "_").ToLower()}.mp4",
            FilePath = $"/videos/{title.Replace(" ", "_").ToLower()}.mp4",
            FileSize = 1024 * 1024,
            Duration = duration ?? TimeSpan.FromMinutes(5),
            Width = 1920,
            Height = 1080,
            ImportedAt = importedAt ?? DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<Tag> AddTagAsync(string name, string? color = null)
    {
        var tag = new Tag { Name = name, Color = color };
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

    private async Task<VideoEntry> AddVideoWithTagsAndCategoriesAsync(
        string title,
        List<Tag>? tags = null,
        List<FolderCategory>? categories = null)
    {
        var video = CreateVideo(title);
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                // Attach if not already tracked, so EF Core doesn't try to re-insert
                var entry = _context.Entry(tag);
                if (entry.State == EntityState.Detached)
                    _context.Tags.Attach(tag);
                video.Tags.Add(tag);
            }
        }
        if (categories != null)
        {
            foreach (var cat in categories)
            {
                var entry = _context.Entry(cat);
                if (entry.State == EntityState.Detached)
                    _context.FolderCategories.Attach(cat);
                video.Categories.Add(cat);
            }
        }
        _context.VideoEntries.Add(video);
        await _context.SaveChangesAsync();
        // Detach all entities so the next query exercises the Include paths
        _context.ChangeTracker.Clear();
        return video;
    }

    #region AsNoTracking — Entities Not Tracked

    [Fact]
    public async Task SearchAsync_ReturnsEntitiesNotTrackedByChangeTracker()
    {
        await AddVideoWithTagsAndCategoriesAsync("Tracked Test Video");

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Single(result.Items);
        // AsNoTracking means the returned entity should NOT be tracked
        var trackedEntries = _context.ChangeTracker.Entries<VideoEntry>().ToList();
        Assert.Empty(trackedEntries);
    }

    [Fact]
    public async Task SearchAsync_MultipleResults_NoneAreTracked()
    {
        await AddVideoWithTagsAndCategoriesAsync("Video A");
        await AddVideoWithTagsAndCategoriesAsync("Video B");
        await AddVideoWithTagsAndCategoriesAsync("Video C");

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        var trackedEntries = _context.ChangeTracker.Entries<VideoEntry>().ToList();
        Assert.Empty(trackedEntries);
    }

    #endregion

    #region AsNoTracking — Tags Included

    [Fact]
    public async Task SearchAsync_WithAsNoTracking_IncludesSingleTag()
    {
        var tag = await AddTagAsync("Action");
        await AddVideoWithTagsAndCategoriesAsync("Action Movie", tags: new List<Tag> { tag });

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Single(result.Items[0].Tags);
        Assert.Equal("Action", result.Items[0].Tags.First().Name);
    }

    [Fact]
    public async Task SearchAsync_WithAsNoTracking_IncludesMultipleTags()
    {
        var tag1 = await AddTagAsync("Action");
        var tag2 = await AddTagAsync("Comedy");
        var tag3 = await AddTagAsync("Drama");
        await AddVideoWithTagsAndCategoriesAsync("Multi-Tag Movie",
            tags: new List<Tag> { tag1, tag2, tag3 });

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(3, result.Items[0].Tags.Count);
        var tagNames = result.Items[0].Tags.Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "Action", "Comedy", "Drama" }, tagNames);
    }

    [Fact]
    public async Task SearchAsync_WithAsNoTracking_VideoWithNoTags_ReturnsEmptyTagCollection()
    {
        await AddVideoWithTagsAndCategoriesAsync("No Tags Video");

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.NotNull(result.Items[0].Tags);
        Assert.Empty(result.Items[0].Tags);
    }

    #endregion

    #region AsNoTracking — Categories Included

    [Fact]
    public async Task SearchAsync_WithAsNoTracking_IncludesSingleCategory()
    {
        var category = await AddCategoryAsync("Favorites");
        await AddVideoWithTagsAndCategoriesAsync("Favorite Video",
            categories: new List<FolderCategory> { category });

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Single(result.Items[0].Categories);
        Assert.Equal("Favorites", result.Items[0].Categories.First().Name);
    }

    [Fact]
    public async Task SearchAsync_WithAsNoTracking_IncludesMultipleCategories()
    {
        var cat1 = await AddCategoryAsync("Favorites");
        var cat2 = await AddCategoryAsync("Tutorials");
        await AddVideoWithTagsAndCategoriesAsync("Categorized Video",
            categories: new List<FolderCategory> { cat1, cat2 });

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(2, result.Items[0].Categories.Count);
        var catNames = result.Items[0].Categories.Select(c => c.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "Favorites", "Tutorials" }, catNames);
    }

    [Fact]
    public async Task SearchAsync_WithAsNoTracking_VideoWithNoCategories_ReturnsEmptyCategoryCollection()
    {
        await AddVideoWithTagsAndCategoriesAsync("No Category Video");

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.NotNull(result.Items[0].Categories);
        Assert.Empty(result.Items[0].Categories);
    }

    #endregion

    #region AsNoTracking — Both Tags and Categories Included

    [Fact]
    public async Task SearchAsync_WithAsNoTracking_IncludesBothTagsAndCategories()
    {
        var tag = await AddTagAsync("Music");
        var category = await AddCategoryAsync("Entertainment");
        await AddVideoWithTagsAndCategoriesAsync("Music Video",
            tags: new List<Tag> { tag },
            categories: new List<FolderCategory> { category });

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Single(result.Items[0].Tags);
        Assert.Equal("Music", result.Items[0].Tags.First().Name);
        Assert.Single(result.Items[0].Categories);
        Assert.Equal("Entertainment", result.Items[0].Categories.First().Name);
    }

    #endregion

    #region AsNoTracking — Correct Results Returned

    [Fact]
    public async Task SearchAsync_WithAsNoTracking_ReturnsCorrectResultCount()
    {
        var tag = await AddTagAsync("Test");
        for (int i = 1; i <= 5; i++)
        {
            await AddVideoWithTagsAndCategoriesAsync($"Video {i}",
                tags: new List<Tag> { tag });
        }

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(5, result.Items.Count);
        Assert.All(result.Items, v => Assert.Single(v.Tags));
    }

    [Fact]
    public async Task SearchAsync_WithAsNoTracking_KeywordFilterStillWorks()
    {
        var tag = await AddTagAsync("Tutorial");
        await AddVideoWithTagsAndCategoriesAsync("C# Tutorial",
            tags: new List<Tag> { tag });
        await AddVideoWithTagsAndCategoriesAsync("Python Guide",
            tags: new List<Tag> { tag });

        var criteria = new SearchCriteria("C#", null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("C# Tutorial", result.Items[0].Title);
        Assert.Single(result.Items[0].Tags);
        Assert.Equal("Tutorial", result.Items[0].Tags.First().Name);
    }

    [Fact]
    public async Task SearchAsync_WithAsNoTracking_TagFilterStillWorks()
    {
        var actionTag = await AddTagAsync("Action");
        var comedyTag = await AddTagAsync("Comedy");
        var category = await AddCategoryAsync("Movies");

        await AddVideoWithTagsAndCategoriesAsync("Action Movie",
            tags: new List<Tag> { actionTag },
            categories: new List<FolderCategory> { category });
        await AddVideoWithTagsAndCategoriesAsync("Comedy Movie",
            tags: new List<Tag> { comedyTag },
            categories: new List<FolderCategory> { category });

        var criteria = new SearchCriteria(null, new List<int> { actionTag.Id }, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Action Movie", result.Items[0].Title);
        Assert.Single(result.Items[0].Tags);
        Assert.Single(result.Items[0].Categories);
    }

    #endregion
}
