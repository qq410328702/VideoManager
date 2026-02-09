using System.Collections.ObjectModel;
using Moq;
using VideoManager.Models;
using VideoManager.Services;
using VideoManager.ViewModels;

namespace VideoManager.Tests.Unit.ViewModels;

public class SearchViewModelTests
{
    private readonly Mock<ISearchService> _searchServiceMock;

    public SearchViewModelTests()
    {
        _searchServiceMock = new Mock<ISearchService>();
    }

    private SearchViewModel CreateViewModel()
    {
        return new SearchViewModel(_searchServiceMock.Object);
    }

    private static PagedResult<VideoEntry> CreatePagedResult(int count, int totalCount, int page, int pageSize)
    {
        var items = Enumerable.Range(1, count).Select(i => new VideoEntry
        {
            Id = (page - 1) * pageSize + i,
            Title = $"Video {(page - 1) * pageSize + i}",
            FileName = $"video{(page - 1) * pageSize + i}.mp4",
            FilePath = $"/videos/video{(page - 1) * pageSize + i}.mp4",
            Duration = TimeSpan.FromMinutes(i),
            ImportedAt = DateTime.UtcNow
        }).ToList();

        return new PagedResult<VideoEntry>(items, totalCount, page, pageSize);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SearchViewModel(null!));
    }

    [Fact]
    public void Constructor_InitializesDefaultValues()
    {
        var vm = CreateViewModel();

        Assert.NotNull(vm.SearchResults);
        Assert.Empty(vm.SearchResults);
        Assert.NotNull(vm.SelectedTagIds);
        Assert.Empty(vm.SelectedTagIds);
        Assert.Equal(string.Empty, vm.Keyword);
        Assert.Null(vm.DateFrom);
        Assert.Null(vm.DateTo);
        Assert.Null(vm.DurationMin);
        Assert.Null(vm.DurationMax);
        Assert.Equal(1, vm.CurrentPage);
        Assert.Equal(0, vm.TotalPages);
        Assert.Equal(0, vm.TotalCount);
        Assert.Equal(20, vm.PageSize);
        Assert.False(vm.IsSearching);
        Assert.Null(vm.SelectedVideo);
    }

    #endregion

    #region Search Tests

    [Fact]
    public async Task SearchAsync_CallsSearchServiceWithCorrectCriteria()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(3, 3, 1, 20));

        var vm = CreateViewModel();
        vm.Keyword = "test";

        await vm.SearchCommand.ExecuteAsync(null);

        _searchServiceMock.Verify(s => s.SearchAsync(
            It.Is<SearchCriteria>(c => c.Keyword == "test"),
            1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_PopulatesSearchResults()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(5, 5, 1, 20));

        var vm = CreateViewModel();
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(5, vm.SearchResults.Count);
        Assert.Equal(5, vm.TotalCount);
        Assert.Equal(1, vm.TotalPages);
        Assert.False(vm.IsSearching);
    }

    [Fact]
    public async Task SearchAsync_SetsIsSearchingDuringExecution()
    {
        var tcs = new TaskCompletionSource<PagedResult<VideoEntry>>();
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        var searchTask = vm.SearchCommand.ExecuteAsync(null);

        Assert.True(vm.IsSearching);

        tcs.SetResult(CreatePagedResult(0, 0, 1, 20));
        await searchTask;

        Assert.False(vm.IsSearching);
    }

    [Fact]
    public async Task SearchAsync_ClearsExistingResultsBeforeSearch()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(3, 3, 1, 20));

        var vm = CreateViewModel();
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.SearchResults.Count);

        // Second search with different results
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(2, 2, 1, 20));

        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.SearchResults.Count);
    }

    [Fact]
    public async Task SearchAsync_EmptyResult_ShowsEmptyList()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<VideoEntry>(new List<VideoEntry>(), 0, 1, 20));

        var vm = CreateViewModel();
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Empty(vm.SearchResults);
        Assert.Equal(0, vm.TotalCount);
        Assert.Equal(0, vm.TotalPages);
    }

    [Fact]
    public async Task SearchAsync_ResetsToPage1()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SearchCriteria _, int page, int size, CancellationToken _) =>
                CreatePagedResult(20, 60, page, size));

        var vm = CreateViewModel();

        // First search loads page 1
        await vm.SearchCommand.ExecuteAsync(null);
        // Navigate to page 2
        await vm.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.CurrentPage);

        // New search should reset to page 1
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.CurrentPage);
    }

    #endregion

    #region Search Criteria Tests (Requirements 4.1-4.5)

    [Fact]
    public async Task SearchAsync_WithKeyword_PassesKeywordInCriteria()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(0, 0, 1, 20));

        var vm = CreateViewModel();
        vm.Keyword = "action movie";

        await vm.SearchCommand.ExecuteAsync(null);

        _searchServiceMock.Verify(s => s.SearchAsync(
            It.Is<SearchCriteria>(c => c.Keyword == "action movie"),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithEmptyKeyword_PassesNullKeyword()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(0, 0, 1, 20));

        var vm = CreateViewModel();
        vm.Keyword = "";

        await vm.SearchCommand.ExecuteAsync(null);

        _searchServiceMock.Verify(s => s.SearchAsync(
            It.Is<SearchCriteria>(c => c.Keyword == null),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithWhitespaceKeyword_PassesNullKeyword()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(0, 0, 1, 20));

        var vm = CreateViewModel();
        vm.Keyword = "   ";

        await vm.SearchCommand.ExecuteAsync(null);

        _searchServiceMock.Verify(s => s.SearchAsync(
            It.Is<SearchCriteria>(c => c.Keyword == null),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithTagIds_PassesTagIdsInCriteria()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(0, 0, 1, 20));

        var vm = CreateViewModel();
        vm.SelectedTagIds.Add(1);
        vm.SelectedTagIds.Add(3);

        await vm.SearchCommand.ExecuteAsync(null);

        _searchServiceMock.Verify(s => s.SearchAsync(
            It.Is<SearchCriteria>(c => c.TagIds != null && c.TagIds.Count == 2 && c.TagIds.Contains(1) && c.TagIds.Contains(3)),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithNoTagIds_PassesNullTagIds()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(0, 0, 1, 20));

        var vm = CreateViewModel();

        await vm.SearchCommand.ExecuteAsync(null);

        _searchServiceMock.Verify(s => s.SearchAsync(
            It.Is<SearchCriteria>(c => c.TagIds == null),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithDateRange_PassesDatesInCriteria()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(0, 0, 1, 20));

        var vm = CreateViewModel();
        var from = new DateTime(2024, 1, 1);
        var to = new DateTime(2024, 12, 31);
        vm.DateFrom = from;
        vm.DateTo = to;

        await vm.SearchCommand.ExecuteAsync(null);

        _searchServiceMock.Verify(s => s.SearchAsync(
            It.Is<SearchCriteria>(c => c.DateFrom == from && c.DateTo == to),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithDurationRange_PassesDurationsInCriteria()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(0, 0, 1, 20));

        var vm = CreateViewModel();
        vm.DurationMin = TimeSpan.FromMinutes(5);
        vm.DurationMax = TimeSpan.FromMinutes(30);

        await vm.SearchCommand.ExecuteAsync(null);

        _searchServiceMock.Verify(s => s.SearchAsync(
            It.Is<SearchCriteria>(c =>
                c.DurationMin == TimeSpan.FromMinutes(5) &&
                c.DurationMax == TimeSpan.FromMinutes(30)),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithMultipleCriteria_PassesAllCriteria()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(0, 0, 1, 20));

        var vm = CreateViewModel();
        vm.Keyword = "test";
        vm.SelectedTagIds.Add(1);
        vm.DateFrom = new DateTime(2024, 1, 1);
        vm.DurationMin = TimeSpan.FromMinutes(5);

        await vm.SearchCommand.ExecuteAsync(null);

        _searchServiceMock.Verify(s => s.SearchAsync(
            It.Is<SearchCriteria>(c =>
                c.Keyword == "test" &&
                c.TagIds != null && c.TagIds.Contains(1) &&
                c.DateFrom == new DateTime(2024, 1, 1) &&
                c.DurationMin == TimeSpan.FromMinutes(5)),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Pagination Tests (Requirement 4.6)

    [Fact]
    public async Task NextPageAsync_IncrementsPageAndSearches()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SearchCriteria _, int page, int size, CancellationToken _) =>
                CreatePagedResult(20, 60, page, size));

        var vm = CreateViewModel();
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.CurrentPage);
        Assert.Equal(3, vm.TotalPages);

        await vm.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.CurrentPage);
        _searchServiceMock.Verify(s => s.SearchAsync(
            It.IsAny<SearchCriteria>(), 2, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PreviousPageAsync_DecrementsPageAndSearches()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SearchCriteria _, int page, int size, CancellationToken _) =>
                CreatePagedResult(20, 60, page, size));

        var vm = CreateViewModel();
        await vm.SearchCommand.ExecuteAsync(null);
        await vm.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.CurrentPage);

        await vm.PreviousPageCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.CurrentPage);
    }

    [Fact]
    public async Task NextPageCommand_CannotExecute_WhenOnLastPage()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(5, 5, 1, 20));

        var vm = CreateViewModel();
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.False(vm.NextPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task PreviousPageCommand_CannotExecute_WhenOnFirstPage()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(20, 60, 1, 20));

        var vm = CreateViewModel();
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.False(vm.PreviousPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task NextPageCommand_CanExecute_WhenNotOnLastPage()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(20, 60, 1, 20));

        var vm = CreateViewModel();
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.True(vm.NextPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task SearchAsync_UsesConfiguredPageSize()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(10, 50, 1, 10));

        var vm = CreateViewModel();
        vm.PageSize = 10;
        await vm.SearchCommand.ExecuteAsync(null);

        _searchServiceMock.Verify(s => s.SearchAsync(
            It.IsAny<SearchCriteria>(), 1, 10, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(5, vm.TotalPages);
    }

    #endregion

    #region ClearFilters Tests

    [Fact]
    public void ClearFilters_ResetsAllCriteria()
    {
        var vm = CreateViewModel();
        vm.Keyword = "test";
        vm.SelectedTagIds.Add(1);
        vm.SelectedTagIds.Add(2);
        vm.DateFrom = DateTime.Now;
        vm.DateTo = DateTime.Now;
        vm.DurationMin = TimeSpan.FromMinutes(5);
        vm.DurationMax = TimeSpan.FromMinutes(30);

        vm.ClearFiltersCommand.Execute(null);

        Assert.Equal(string.Empty, vm.Keyword);
        Assert.Empty(vm.SelectedTagIds);
        Assert.Null(vm.DateFrom);
        Assert.Null(vm.DateTo);
        Assert.Null(vm.DurationMin);
        Assert.Null(vm.DurationMax);
    }

    [Fact]
    public async Task ClearFilters_ClearsSearchResults()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(5, 5, 1, 20));

        var vm = CreateViewModel();
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Equal(5, vm.SearchResults.Count);

        vm.ClearFiltersCommand.Execute(null);

        Assert.Empty(vm.SearchResults);
        Assert.Equal(0, vm.TotalCount);
        Assert.Equal(0, vm.TotalPages);
        Assert.Equal(1, vm.CurrentPage);
    }

    [Fact]
    public void ClearFilters_ResetsSelectedVideo()
    {
        var vm = CreateViewModel();
        vm.SelectedVideo = new VideoEntry { Id = 1, Title = "Test" };

        vm.ClearFiltersCommand.Execute(null);

        Assert.Null(vm.SelectedVideo);
    }

    #endregion

    #region BuildSearchCriteria Tests

    [Fact]
    public void BuildSearchCriteria_WithAllFilters_ReturnsCorrectCriteria()
    {
        var vm = CreateViewModel();
        vm.Keyword = "test";
        vm.SelectedTagIds.Add(1);
        vm.SelectedTagIds.Add(2);
        vm.DateFrom = new DateTime(2024, 1, 1);
        vm.DateTo = new DateTime(2024, 12, 31);
        vm.DurationMin = TimeSpan.FromMinutes(5);
        vm.DurationMax = TimeSpan.FromHours(2);

        var criteria = vm.BuildSearchCriteria();

        Assert.Equal("test", criteria.Keyword);
        Assert.NotNull(criteria.TagIds);
        Assert.Equal(2, criteria.TagIds.Count);
        Assert.Contains(1, criteria.TagIds);
        Assert.Contains(2, criteria.TagIds);
        Assert.Equal(new DateTime(2024, 1, 1), criteria.DateFrom);
        Assert.Equal(new DateTime(2024, 12, 31), criteria.DateTo);
        Assert.Equal(TimeSpan.FromMinutes(5), criteria.DurationMin);
        Assert.Equal(TimeSpan.FromHours(2), criteria.DurationMax);
    }

    [Fact]
    public void BuildSearchCriteria_WithNoFilters_ReturnsAllNulls()
    {
        var vm = CreateViewModel();

        var criteria = vm.BuildSearchCriteria();

        Assert.Null(criteria.Keyword);
        Assert.Null(criteria.TagIds);
        Assert.Null(criteria.DateFrom);
        Assert.Null(criteria.DateTo);
        Assert.Null(criteria.DurationMin);
        Assert.Null(criteria.DurationMax);
    }

    #endregion

    #region CalculateTotalPages Tests

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(100, 20, 5)]
    [InlineData(101, 20, 6)]
    public void CalculateTotalPages_ReturnsCorrectValue(int totalCount, int pageSize, int expected)
    {
        Assert.Equal(expected, SearchViewModel.CalculateTotalPages(totalCount, pageSize));
    }

    [Fact]
    public void CalculateTotalPages_ZeroPageSize_ReturnsZero()
    {
        Assert.Equal(0, SearchViewModel.CalculateTotalPages(100, 0));
    }

    #endregion

    #region SelectedVideo Tests

    [Fact]
    public void SelectedVideo_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.SelectedVideo))
                raised = true;
        };

        vm.SelectedVideo = new VideoEntry { Id = 1, Title = "Test" };

        Assert.True(raised);
    }

    [Fact]
    public void SelectedVideo_CanBeSetToNull()
    {
        var vm = CreateViewModel();
        vm.SelectedVideo = new VideoEntry { Id = 1, Title = "Test" };
        vm.SelectedVideo = null;

        Assert.Null(vm.SelectedVideo);
    }

    #endregion

    #region IsSearching Guard Tests

    [Fact]
    public async Task SearchCommand_CannotExecute_WhileSearching()
    {
        var tcs = new TaskCompletionSource<PagedResult<VideoEntry>>();
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        var searchTask = vm.SearchCommand.ExecuteAsync(null);

        Assert.False(vm.SearchCommand.CanExecute(null));

        tcs.SetResult(CreatePagedResult(0, 0, 1, 20));
        await searchTask;

        Assert.True(vm.SearchCommand.CanExecute(null));
    }

    [Fact]
    public async Task NextPageCommand_CannotExecute_WhileSearching()
    {
        // First, do a search to get multiple pages
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(20, 60, 1, 20));

        var vm = CreateViewModel();
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.True(vm.NextPageCommand.CanExecute(null));

        // Now start a new search that blocks
        var tcs = new TaskCompletionSource<PagedResult<VideoEntry>>();
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var searchTask = vm.SearchCommand.ExecuteAsync(null);

        Assert.False(vm.NextPageCommand.CanExecute(null));

        tcs.SetResult(CreatePagedResult(20, 60, 1, 20));
        await searchTask;
    }

    #endregion

    #region PropertyChanged Tests

    [Fact]
    public void Keyword_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Keyword))
                raised = true;
        };

        vm.Keyword = "new keyword";

        Assert.True(raised);
    }

    [Fact]
    public void DateFrom_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.DateFrom))
                raised = true;
        };

        vm.DateFrom = DateTime.Now;

        Assert.True(raised);
    }

    [Fact]
    public void DurationMin_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.DurationMin))
                raised = true;
        };

        vm.DurationMin = TimeSpan.FromMinutes(5);

        Assert.True(raised);
    }

    #endregion
}
