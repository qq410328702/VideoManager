using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;
using VideoManager.ViewModels;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for debounced search behavior.
/// Tests Property 1: 防抖搜索行为
///
/// **Feature: video-manager-optimization, Property 1: 防抖搜索行为**
/// **Validates: Requirements 2.1, 2.2, 2.3**
///
/// For any input sequence and time intervals, search should only execute
/// when 300ms have elapsed since the last input with no new input.
/// Consecutive inputs within 300ms should reset the timer, and new input
/// arriving should cancel any in-progress search request.
/// </summary>
public class DebouncePropertyTests
{
    private static PagedResult<VideoEntry> CreatePagedResult(int count)
    {
        var items = Enumerable.Range(1, count).Select(i => new VideoEntry
        {
            Id = i,
            Title = $"Video {i}",
            FileName = $"video{i}.mp4",
            FilePath = $"/videos/video{i}.mp4",
            Duration = TimeSpan.FromMinutes(i),
            ImportedAt = DateTime.UtcNow
        }).ToList();

        return new PagedResult<VideoEntry>(items, count, 1, 20);
    }

    private static (MainViewModel vm, Mock<ISearchService> searchMock, Mock<IVideoRepository> repoMock) CreateTestSetup()
    {
        var videoRepoMock = new Mock<IVideoRepository>();
        var searchServiceMock = new Mock<ISearchService>();
        var categoryRepoMock = new Mock<ICategoryRepository>();
        var tagRepoMock = new Mock<ITagRepository>();

        videoRepoMock
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
                It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(5));

        searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SearchCriteria c, int p, int ps, CancellationToken ct) =>
                CreatePagedResult(2));

        categoryRepoMock
            .Setup(r => r.GetTreeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FolderCategory>());
        tagRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag>());

        var videoListVm = new VideoListViewModel(videoRepoMock.Object);
        var searchVm = new SearchViewModel(searchServiceMock.Object);
        var categoryVm = new CategoryViewModel(categoryRepoMock.Object, tagRepoMock.Object);
        var fileWatcherMock = new Mock<IFileWatcherService>();
        var navigationServiceMock = new Mock<INavigationService>();
        var dialogServiceMock = new Mock<IDialogService>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        var options = Options.Create(new VideoManagerOptions
        {
            VideoLibraryPath = "/test/videos",
            ThumbnailDirectory = "/test/thumbnails"
        });
        var vm = new MainViewModel(videoListVm, searchVm, categoryVm, fileWatcherMock.Object, options,
            navigationServiceMock.Object, dialogServiceMock.Object, serviceProviderMock.Object);

        return (vm, searchServiceMock, videoRepoMock);
    }

    /// <summary>
    /// Generates a positive delay in milliseconds that is well within the debounce window (1-200ms).
    /// </summary>
    private static FsCheck.Arbitrary<int> ShortDelayArb()
    {
        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Choose(1, 200));
    }

    /// <summary>
    /// Generates a non-empty keyword string of 1-20 alphanumeric characters.
    /// </summary>
    private static FsCheck.Arbitrary<string> KeywordArb()
    {
        var gen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.Choose(1, 20),
            length =>
            {
                var chars = "abcdefghijklmnopqrstuvwxyz0123456789";
                var rng = new Random();
                return new string(Enumerable.Range(0, length)
                    .Select(_ => chars[rng.Next(chars.Length)])
                    .ToArray());
            });

        return FsCheck.Fluent.Arb.From(gen);
    }

    /// <summary>
    /// Generates a sequence of 2-6 distinct keyword strings for rapid input simulation.
    /// </summary>
    private static FsCheck.Arbitrary<string[]> KeywordSequenceArb()
    {
        var gen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.Choose(2, 6),
            count =>
            {
                var rng = new Random();
                var chars = "abcdefghijklmnopqrstuvwxyz";
                return Enumerable.Range(0, count)
                    .Select(i =>
                    {
                        var len = rng.Next(2, 10);
                        return new string(Enumerable.Range(0, len)
                            .Select(_ => chars[rng.Next(chars.Length)])
                            .ToArray());
                    })
                    .ToArray();
            });

        return FsCheck.Fluent.Arb.From(gen);
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 1: 防抖搜索行为**
    /// **Validates: Requirements 2.1, 2.2, 2.3**
    ///
    /// Sub-property A: For any non-empty keyword, after setting SearchKeyword and waiting
    /// longer than 300ms, the search service should be called exactly once with that keyword.
    /// This validates Requirement 2.1: search executes after 300ms of no input.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 50)]
    public FsCheck.Property SearchExecutesAfterDebounceDelay()
    {
        return FsCheck.Fluent.Prop.ForAll(KeywordArb(), keyword =>
        {
            var (vm, searchMock, _) = CreateTestSetup();

            // Set keyword - triggers debounced search
            vm.SearchKeyword = keyword;

            // Wait for debounce (300ms) + buffer
            Task.Delay(800).GetAwaiter().GetResult();

            // Verify search was called exactly once with the correct keyword
            searchMock.Verify(
                s => s.SearchAsync(
                    It.Is<SearchCriteria>(c => c.Keyword == keyword),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);

            return true;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 1: 防抖搜索行为**
    /// **Validates: Requirements 2.1, 2.2, 2.3**
    ///
    /// Sub-property B: For any sequence of rapid keyword inputs (each within 300ms of the
    /// previous), only the last keyword should trigger a search execution.
    /// This validates Requirement 2.2: consecutive inputs within 300ms reset the timer.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 30)]
    public FsCheck.Property RapidInputsOnlyTriggerSearchForLastKeyword()
    {
        return FsCheck.Fluent.Prop.ForAll(KeywordSequenceArb(), keywords =>
        {
            var (vm, searchMock, _) = CreateTestSetup();

            // Rapidly set keywords with short delays (well within 300ms debounce)
            foreach (var kw in keywords)
            {
                vm.SearchKeyword = kw;
                Task.Delay(50).GetAwaiter().GetResult(); // 50ms between inputs, well within 300ms
            }

            // Wait for debounce to complete after the last input
            Task.Delay(500).GetAwaiter().GetResult();

            var lastKeyword = keywords.Last();

            // Verify: search was called with the last keyword
            searchMock.Verify(
                s => s.SearchAsync(
                    It.Is<SearchCriteria>(c => c.Keyword == lastKeyword),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);

            // Verify: intermediate keywords did NOT trigger search
            foreach (var kw in keywords.Take(keywords.Length - 1))
            {
                // Only check if the intermediate keyword is different from the last one
                if (kw != lastKeyword)
                {
                    searchMock.Verify(
                        s => s.SearchAsync(
                            It.Is<SearchCriteria>(c => c.Keyword == kw),
                            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                        Times.Never);
                }
            }

            return true;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 1: 防抖搜索行为**
    /// **Validates: Requirements 2.1, 2.2, 2.3**
    ///
    /// Sub-property C: For any keyword, if a new keyword is set before the debounce
    /// timer expires, the search for the first keyword should be cancelled (never executed)
    /// and only the second keyword's search should execute after its own 300ms debounce.
    /// This validates Requirement 2.3: new input cancels in-progress debounce/search.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 50)]
    public FsCheck.Property NewInputCancelsPreviousDebounce()
    {
        return FsCheck.Fluent.Prop.ForAll(KeywordArb(), KeywordArb(), (keyword1, keyword2) =>
        {
            // Ensure keywords are different to make the test meaningful
            if (keyword1 == keyword2) return true;

            var (vm, searchMock, _) = CreateTestSetup();

            // Set first keyword
            vm.SearchKeyword = keyword1;

            // Wait a short time (less than 300ms) then set second keyword
            Task.Delay(100).GetAwaiter().GetResult();
            vm.SearchKeyword = keyword2;

            // Wait for debounce to complete
            Task.Delay(500).GetAwaiter().GetResult();

            // Verify: first keyword search was never executed
            searchMock.Verify(
                s => s.SearchAsync(
                    It.Is<SearchCriteria>(c => c.Keyword == keyword1),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);

            // Verify: second keyword search was executed exactly once
            searchMock.Verify(
                s => s.SearchAsync(
                    It.Is<SearchCriteria>(c => c.Keyword == keyword2),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);

            return true;
        });
    }
}
