using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

public class SearchServiceTests : IDisposable
{
    private readonly VideoManagerDbContext _context;
    private readonly SearchService _service;

    public SearchServiceTests()
    {
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _context = new VideoManagerDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        var mockMetrics = new Mock<IMetricsService>();
        mockMetrics.Setup(m => m.StartTimer(It.IsAny<string>())).Returns(new NoOpDisposable());
        _service = new SearchService(_context, mockMetrics.Object, NullLogger<SearchService>.Instance);
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
            FilePath = $"/videos/{fileName ?? title.ToLower()}.mp4",
            FileSize = 1024 * 1024,
            Duration = duration ?? TimeSpan.FromMinutes(5),
            Width = 1920,
            Height = 1080,
            ImportedAt = importedAt ?? DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<VideoEntry> AddVideoAsync(
        string title,
        string? description = null,
        TimeSpan? duration = null,
        DateTime? importedAt = null,
        List<Tag>? tags = null)
    {
        var video = CreateVideo(title, description, duration, importedAt);
        if (tags != null)
        {
            foreach (var tag in tags)
                video.Tags.Add(tag);
        }
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

    #region Argument Validation

    [Fact]
    public async Task SearchAsync_NullCriteria_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.SearchAsync(null!, 1, 10, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SearchAsync_InvalidPage_ThrowsArgumentOutOfRangeException(int page)
    {
        var criteria = new SearchCriteria(null, null, null, null, null, null);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.SearchAsync(criteria, page, 10, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SearchAsync_InvalidPageSize_ThrowsArgumentOutOfRangeException(int pageSize)
    {
        var criteria = new SearchCriteria(null, null, null, null, null, null);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.SearchAsync(criteria, 1, pageSize, CancellationToken.None));
    }

    #endregion

    #region No Filters — Returns All

    [Fact]
    public async Task SearchAsync_NoFilters_ReturnsAllVideos()
    {
        await AddVideoAsync("Video A");
        await AddVideoAsync("Video B");
        await AddVideoAsync("Video C");

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task SearchAsync_EmptyDatabase_ReturnsEmptyResult()
    {
        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    #endregion

    #region Keyword Search

    [Fact]
    public async Task SearchAsync_KeywordMatchesTitle_ReturnsMatchingVideos()
    {
        await AddVideoAsync("Funny Cat Compilation");
        await AddVideoAsync("Dog Training Guide");
        await AddVideoAsync("Cat vs Dog");

        var criteria = new SearchCriteria("Cat", null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, v => Assert.Contains("Cat", v.Title));
    }

    [Fact]
    public async Task SearchAsync_KeywordMatchesDescription_ReturnsMatchingVideos()
    {
        await AddVideoAsync("Video A", description: "A tutorial about cooking");
        await AddVideoAsync("Video B", description: "A guide to gardening");
        await AddVideoAsync("Video C", description: "Cooking recipes for beginners");

        var criteria = new SearchCriteria("cooking", null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_KeywordCaseInsensitive_ReturnsMatches()
    {
        await AddVideoAsync("UPPERCASE TITLE");
        await AddVideoAsync("lowercase title");
        await AddVideoAsync("Mixed Case Title");

        var criteria = new SearchCriteria("title", null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_KeywordNoMatch_ReturnsEmpty()
    {
        await AddVideoAsync("Video A");
        await AddVideoAsync("Video B");

        var criteria = new SearchCriteria("nonexistent", null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SearchAsync_WhitespaceKeyword_ReturnsAllVideos()
    {
        await AddVideoAsync("Video A");
        await AddVideoAsync("Video B");

        var criteria = new SearchCriteria("   ", null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
    }

    #endregion

    #region Tag Filter

    [Fact]
    public async Task SearchAsync_TagFilter_ReturnsVideosWithMatchingTag()
    {
        var actionTag = await AddTagAsync("Action");
        var comedyTag = await AddTagAsync("Comedy");

        await AddVideoAsync("Action Movie", tags: new List<Tag> { actionTag });
        await AddVideoAsync("Comedy Show", tags: new List<Tag> { comedyTag });
        await AddVideoAsync("Action Comedy", tags: new List<Tag> { actionTag, comedyTag });

        var criteria = new SearchCriteria(null, new List<int> { actionTag.Id }, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, v => Assert.Contains(v.Tags, t => t.Id == actionTag.Id));
    }

    [Fact]
    public async Task SearchAsync_MultipleTagIds_ReturnsVideosWithAnyOfThem()
    {
        var tag1 = await AddTagAsync("Tag1");
        var tag2 = await AddTagAsync("Tag2");
        var tag3 = await AddTagAsync("Tag3");

        await AddVideoAsync("Video with Tag1", tags: new List<Tag> { tag1 });
        await AddVideoAsync("Video with Tag2", tags: new List<Tag> { tag2 });
        await AddVideoAsync("Video with Tag3", tags: new List<Tag> { tag3 });

        var criteria = new SearchCriteria(null, new List<int> { tag1.Id, tag2.Id }, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_EmptyTagIds_ReturnsAllVideos()
    {
        await AddVideoAsync("Video A");
        await AddVideoAsync("Video B");

        var criteria = new SearchCriteria(null, new List<int>(), null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
    }

    #endregion

    #region Date Range Filter

    [Fact]
    public async Task SearchAsync_DateFromFilter_ReturnsVideosAfterDate()
    {
        await AddVideoAsync("Old Video", importedAt: new DateTime(2023, 1, 1));
        await AddVideoAsync("New Video", importedAt: new DateTime(2024, 6, 1));
        await AddVideoAsync("Newest Video", importedAt: new DateTime(2024, 12, 1));

        var criteria = new SearchCriteria(null, null, new DateTime(2024, 1, 1), null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, v => Assert.True(v.ImportedAt >= new DateTime(2024, 1, 1)));
    }

    [Fact]
    public async Task SearchAsync_DateToFilter_ReturnsVideosBeforeDate()
    {
        await AddVideoAsync("Old Video", importedAt: new DateTime(2023, 1, 1));
        await AddVideoAsync("Mid Video", importedAt: new DateTime(2024, 6, 1));
        await AddVideoAsync("New Video", importedAt: new DateTime(2025, 1, 1));

        var criteria = new SearchCriteria(null, null, null, new DateTime(2024, 6, 1), null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, v => Assert.True(v.ImportedAt <= new DateTime(2024, 6, 1)));
    }

    [Fact]
    public async Task SearchAsync_DateRange_ReturnsVideosWithinRange()
    {
        await AddVideoAsync("Before", importedAt: new DateTime(2023, 1, 1));
        await AddVideoAsync("Within", importedAt: new DateTime(2024, 6, 15));
        await AddVideoAsync("After", importedAt: new DateTime(2025, 12, 1));

        var criteria = new SearchCriteria(null, null, new DateTime(2024, 1, 1), new DateTime(2024, 12, 31), null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Within", result.Items[0].Title);
    }

    #endregion

    #region Duration Range Filter

    [Fact]
    public async Task SearchAsync_DurationMinFilter_ReturnsVideosAboveMinDuration()
    {
        await AddVideoAsync("Short", duration: TimeSpan.FromMinutes(2));
        await AddVideoAsync("Medium", duration: TimeSpan.FromMinutes(10));
        await AddVideoAsync("Long", duration: TimeSpan.FromMinutes(60));

        var criteria = new SearchCriteria(null, null, null, null, TimeSpan.FromMinutes(5), null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, v => Assert.True(v.Duration >= TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task SearchAsync_DurationMaxFilter_ReturnsVideosBelowMaxDuration()
    {
        await AddVideoAsync("Short", duration: TimeSpan.FromMinutes(2));
        await AddVideoAsync("Medium", duration: TimeSpan.FromMinutes(10));
        await AddVideoAsync("Long", duration: TimeSpan.FromMinutes(60));

        var criteria = new SearchCriteria(null, null, null, null, null, TimeSpan.FromMinutes(15));
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, v => Assert.True(v.Duration <= TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public async Task SearchAsync_DurationRange_ReturnsVideosWithinRange()
    {
        await AddVideoAsync("Short", duration: TimeSpan.FromMinutes(1));
        await AddVideoAsync("Medium", duration: TimeSpan.FromMinutes(10));
        await AddVideoAsync("Long", duration: TimeSpan.FromHours(2));

        var criteria = new SearchCriteria(null, null, null, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Medium", result.Items[0].Title);
    }

    #endregion

    #region Multiple Conditions — Intersection (AND)

    [Fact]
    public async Task SearchAsync_KeywordAndTag_ReturnsIntersection()
    {
        var actionTag = await AddTagAsync("Action");

        await AddVideoAsync("Action Movie", tags: new List<Tag> { actionTag });
        await AddVideoAsync("Action Documentary", description: "A documentary about action sports", tags: new List<Tag> { actionTag });
        await AddVideoAsync("Comedy Movie");

        var criteria = new SearchCriteria("Movie", new List<int> { actionTag.Id }, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Action Movie", result.Items[0].Title);
    }

    [Fact]
    public async Task SearchAsync_AllFilters_ReturnsIntersection()
    {
        var tag = await AddTagAsync("Tutorial");

        // This video matches all criteria
        await AddVideoAsync("C# Tutorial",
            description: "Learn C# programming",
            duration: TimeSpan.FromMinutes(30),
            importedAt: new DateTime(2024, 6, 1),
            tags: new List<Tag> { tag });

        // Matches keyword and tag but not date
        await AddVideoAsync("C# Advanced Tutorial",
            duration: TimeSpan.FromMinutes(45),
            importedAt: new DateTime(2022, 1, 1),
            tags: new List<Tag> { tag });

        // Matches keyword and date but not tag
        await AddVideoAsync("C# Basics",
            duration: TimeSpan.FromMinutes(20),
            importedAt: new DateTime(2024, 3, 1));

        // Matches nothing
        await AddVideoAsync("Python Guide",
            duration: TimeSpan.FromMinutes(15),
            importedAt: new DateTime(2024, 5, 1));

        var criteria = new SearchCriteria(
            "C#",
            new List<int> { tag.Id },
            new DateTime(2024, 1, 1),
            new DateTime(2024, 12, 31),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromHours(1));

        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("C# Tutorial", result.Items[0].Title);
    }

    #endregion

    #region Pagination

    [Fact]
    public async Task SearchAsync_Pagination_ReturnsCorrectPage()
    {
        for (int i = 1; i <= 10; i++)
        {
            await AddVideoAsync($"Video {i:D2}", importedAt: new DateTime(2024, 1, i));
        }

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 2, 3, CancellationToken.None);

        Assert.Equal(10, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(3, result.PageSize);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task SearchAsync_LastPage_ReturnsRemainingItems()
    {
        for (int i = 1; i <= 5; i++)
        {
            await AddVideoAsync($"Video {i}", importedAt: new DateTime(2024, 1, i));
        }

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 3, 2, CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task SearchAsync_PageBeyondData_ReturnsEmptyItems()
    {
        await AddVideoAsync("Only Video");

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 5, 10, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Empty(result.Items);
    }

    #endregion

    #region Includes Tags and Categories

    [Fact]
    public async Task SearchAsync_IncludesTagsInResults()
    {
        var tag = await AddTagAsync("Music");
        await AddVideoAsync("Music Video", tags: new List<Tag> { tag });

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Single(result.Items[0].Tags);
        Assert.Equal("Music", result.Items[0].Tags.First().Name);
    }

    [Fact]
    public async Task SearchAsync_IncludesCategoriesInResults()
    {
        var category = new FolderCategory { Name = "Favorites" };
        _context.FolderCategories.Add(category);
        await _context.SaveChangesAsync();

        var video = CreateVideo("My Video");
        video.Categories.Add(category);
        _context.VideoEntries.Add(video);
        await _context.SaveChangesAsync();

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Single(result.Items[0].Categories);
        Assert.Equal("Favorites", result.Items[0].Categories.First().Name);
    }

    #endregion

    #region Ordering

    [Fact]
    public async Task SearchAsync_ResultsOrderedByImportedAtDescending()
    {
        await AddVideoAsync("Oldest", importedAt: new DateTime(2023, 1, 1));
        await AddVideoAsync("Middle", importedAt: new DateTime(2024, 1, 1));
        await AddVideoAsync("Newest", importedAt: new DateTime(2025, 1, 1));

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        var result = await _service.SearchAsync(criteria, 1, 10, CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal("Newest", result.Items[0].Title);
        Assert.Equal("Middle", result.Items[1].Title);
        Assert.Equal("Oldest", result.Items[2].Title);
    }

    #endregion

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
