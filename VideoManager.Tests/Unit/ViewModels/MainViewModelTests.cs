using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;
using VideoManager.ViewModels;

namespace VideoManager.Tests.Unit.ViewModels;

[Collection("Messenger")]
public class MainViewModelTests : IDisposable
{
    private readonly Mock<IVideoRepository> _videoRepoMock;
    private readonly Mock<ISearchService> _searchServiceMock;
    private readonly Mock<ICategoryRepository> _categoryRepoMock;
    private readonly Mock<ITagRepository> _tagRepoMock;
    private readonly Mock<IFileWatcherService> _fileWatcherMock;
    private readonly Mock<INavigationService> _navigationServiceMock;
    private readonly Mock<IDialogService> _dialogServiceMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly IOptions<VideoManagerOptions> _options;

    public MainViewModelTests()
    {
        _videoRepoMock = new Mock<IVideoRepository>();
        _searchServiceMock = new Mock<ISearchService>();
        _categoryRepoMock = new Mock<ICategoryRepository>();
        _tagRepoMock = new Mock<ITagRepository>();
        _fileWatcherMock = new Mock<IFileWatcherService>();
        _navigationServiceMock = new Mock<INavigationService>();
        _dialogServiceMock = new Mock<IDialogService>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _options = Options.Create(new VideoManagerOptions
        {
            VideoLibraryPath = "/test/videos",
            ThumbnailDirectory = "/test/thumbnails"
        });

        // Reset messenger between tests
        WeakReferenceMessenger.Default.Reset();
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.Reset();
    }

    private MainViewModel CreateViewModel()
    {
        var videoListVm = new VideoListViewModel(_videoRepoMock.Object);
        var searchVm = new SearchViewModel(_searchServiceMock.Object);
        var categoryVm = new CategoryViewModel(_categoryRepoMock.Object, _tagRepoMock.Object);
        var paginationVm = new PaginationViewModel(videoListVm);
        var sortVm = new SortViewModel();
        var batchOperationVm = new BatchOperationViewModel(videoListVm, categoryVm, _dialogServiceMock.Object, _serviceProviderMock.Object);
        return new MainViewModel(videoListVm, searchVm, categoryVm, paginationVm, sortVm, batchOperationVm,
            _fileWatcherMock.Object, _options,
            _navigationServiceMock.Object, _dialogServiceMock.Object, _serviceProviderMock.Object);
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

    private void SetupDefaultVideoRepo(int count = 5, int totalCount = 5)
    {
        _videoRepoMock
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync((int page, int size, CancellationToken _, SortField __, SortDirection ___) =>
                CreatePagedResult(Math.Min(count, size), totalCount, page, size));
    }

    private void SetupDefaultCategoryAndTags()
    {
        _categoryRepoMock
            .Setup(r => r.GetTreeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FolderCategory>());
        _tagRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag>());
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullVideoListVm_ThrowsArgumentNullException()
    {
        var searchVm = new SearchViewModel(_searchServiceMock.Object);
        var categoryVm = new CategoryViewModel(_categoryRepoMock.Object, _tagRepoMock.Object);
        var videoListVm = new VideoListViewModel(_videoRepoMock.Object);
        var paginationVm = new PaginationViewModel(videoListVm);
        var sortVm = new SortViewModel();
        var batchOperationVm = new BatchOperationViewModel(videoListVm, categoryVm, _dialogServiceMock.Object, _serviceProviderMock.Object);

        Assert.Throws<ArgumentNullException>(() => new MainViewModel(null!, searchVm, categoryVm, paginationVm, sortVm, batchOperationVm,
            _fileWatcherMock.Object, _options,
            _navigationServiceMock.Object, _dialogServiceMock.Object, _serviceProviderMock.Object));
    }

    [Fact]
    public void Constructor_WithNullSearchVm_ThrowsArgumentNullException()
    {
        var videoListVm = new VideoListViewModel(_videoRepoMock.Object);
        var categoryVm = new CategoryViewModel(_categoryRepoMock.Object, _tagRepoMock.Object);
        var paginationVm = new PaginationViewModel(videoListVm);
        var sortVm = new SortViewModel();
        var batchOperationVm = new BatchOperationViewModel(videoListVm, categoryVm, _dialogServiceMock.Object, _serviceProviderMock.Object);

        Assert.Throws<ArgumentNullException>(() => new MainViewModel(videoListVm, null!, categoryVm, paginationVm, sortVm, batchOperationVm,
            _fileWatcherMock.Object, _options,
            _navigationServiceMock.Object, _dialogServiceMock.Object, _serviceProviderMock.Object));
    }

    [Fact]
    public void Constructor_WithNullCategoryVm_ThrowsArgumentNullException()
    {
        var videoListVm = new VideoListViewModel(_videoRepoMock.Object);
        var searchVm = new SearchViewModel(_searchServiceMock.Object);
        var paginationVm = new PaginationViewModel(videoListVm);
        var sortVm = new SortViewModel();
        var batchOperationVm = new BatchOperationViewModel(videoListVm, new CategoryViewModel(_categoryRepoMock.Object, _tagRepoMock.Object), _dialogServiceMock.Object, _serviceProviderMock.Object);

        Assert.Throws<ArgumentNullException>(() => new MainViewModel(videoListVm, searchVm, null!, paginationVm, sortVm, batchOperationVm,
            _fileWatcherMock.Object, _options,
            _navigationServiceMock.Object, _dialogServiceMock.Object, _serviceProviderMock.Object));
    }

    [Fact]
    public void Constructor_InitializesDefaultValues()
    {
        var vm = CreateViewModel();

        Assert.Equal("就绪", vm.StatusText);
        Assert.Equal(string.Empty, vm.SearchKeyword);
        Assert.NotNull(vm.VideoListVm);
        Assert.NotNull(vm.SearchVm);
        Assert.NotNull(vm.CategoryVm);
        Assert.NotNull(vm.PaginationVm);
        Assert.NotNull(vm.SortVm);
        Assert.NotNull(vm.BatchOperationVm);
    }

    #endregion

    #region Initialize Tests

    [Fact]
    public async Task InitializeAsync_LoadsVideosAndCategoriesAndTags()
    {
        SetupDefaultVideoRepo();
        SetupDefaultCategoryAndTags();

        var vm = CreateViewModel();
        await vm.InitializeCommand.ExecuteAsync(null);

        _videoRepoMock.Verify(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()), Times.Once);
        _categoryRepoMock.Verify(r => r.GetTreeAsync(It.IsAny<CancellationToken>()), Times.Once);
        _tagRepoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_SetsStatusTextDuringLoading()
    {
        var statusChanges = new List<string>();
        var tcs = new TaskCompletionSource<PagedResult<VideoEntry>>();

        _videoRepoMock
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .Returns(tcs.Task);
        SetupDefaultCategoryAndTags();

        var vm = CreateViewModel();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StatusText))
                statusChanges.Add(vm.StatusText);
        };

        var initTask = vm.InitializeCommand.ExecuteAsync(null);

        // Should show loading status
        Assert.Contains("正在加载数据...", statusChanges);

        tcs.SetResult(CreatePagedResult(5, 5, 1, 50));
        await initTask;

        // Should show ready status after completion
        Assert.Equal("就绪", vm.StatusText);
    }

    [Fact]
    public async Task InitializeAsync_SyncsPaginationAfterLoading()
    {
        SetupDefaultVideoRepo(count: 5, totalCount: 5);
        SetupDefaultCategoryAndTags();

        var vm = CreateViewModel();
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Contains("5", vm.PaginationVm.PageInfoText);
    }

    [Fact]
    public async Task InitializeAsync_OnError_SetsFailureStatusText()
    {
        _videoRepoMock
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ThrowsAsync(new Exception("Database error"));
        SetupDefaultCategoryAndTags();

        var vm = CreateViewModel();
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Contains("加载失败", vm.StatusText);
        Assert.Contains("Database error", vm.StatusText);
    }

    #endregion

    #region Search Command Tests

    [Fact]
    public async Task SearchAsync_SetsSearchKeywordOnSearchVm()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(3, 3, 1, 20));

        var vm = CreateViewModel();
        vm.SearchKeyword = "test video";

        // Use SearchCommand directly (bypasses debounce)
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal("test video", vm.SearchVm.Keyword);
    }

    [Fact]
    public async Task SearchAsync_UpdatesVideoListWithSearchResults()
    {
        var searchResults = CreatePagedResult(3, 3, 1, 20);
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        var vm = CreateViewModel();
        vm.SearchKeyword = "test";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.VideoListVm.Videos.Count);
        Assert.Equal(3, vm.VideoListVm.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_UpdatesStatusTextWithResultCount()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(5, 10, 1, 20));

        var vm = CreateViewModel();
        vm.SearchKeyword = "test";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Contains("10", vm.StatusText);
        Assert.Contains("搜索完成", vm.StatusText);
    }

    [Fact]
    public async Task SearchAsync_ShowsSearchingStatusDuringExecution()
    {
        var statusChanges = new List<string>();
        var tcs = new TaskCompletionSource<PagedResult<VideoEntry>>();

        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        vm.SearchKeyword = "test";
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StatusText))
                statusChanges.Add(vm.StatusText);
        };

        var searchTask = vm.SearchCommand.ExecuteAsync(null);

        Assert.Contains("正在搜索...", statusChanges);

        tcs.SetResult(CreatePagedResult(3, 3, 1, 20));
        await searchTask;
    }

    #endregion

    #region Debounced Search Tests

    [Fact]
    public async Task SearchKeywordChanged_TriggersSearchAfterDebounce()
    {
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(2, 2, 1, 20));
        SetupDefaultVideoRepo();

        var vm = CreateViewModel();
        vm.SearchKeyword = "hello";

        // Poll for debounce completion (300ms debounce + execution time)
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(100);
            if (_searchServiceMock.Invocations.Count > 0) break;
        }

        _searchServiceMock.Verify(
            s => s.SearchAsync(It.Is<SearchCriteria>(c => c.Keyword == "hello"), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchKeywordChanged_EmptyKeyword_RefreshesVideoList()
    {
        SetupDefaultVideoRepo(count: 5, totalCount: 5);
        _searchServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<SearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePagedResult(2, 2, 1, 20));

        var vm = CreateViewModel();

        // Directly invoke search to simulate keyword "test" search completing
        vm.SearchKeyword = "test";
        await vm.SearchCommand.ExecuteAsync(null);

        // Now clear the keyword - this should trigger a refresh (LoadVideos) via debounce
        _videoRepoMock.Invocations.Clear();
        vm.SearchKeyword = "";

        // Wait for debounced refresh to complete (300ms debounce + execution time)
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(100);
            if (_videoRepoMock.Invocations.Count > 0) break;
        }

        // Empty keyword should trigger a refresh (LoadVideos), not a search
        _videoRepoMock.Verify(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()), Times.Once);
    }

    #endregion

    #region ClearFilters Tests

    [Fact]
    public async Task ClearFiltersAsync_ResetsSearchKeyword()
    {
        SetupDefaultVideoRepo();

        var vm = CreateViewModel();
        vm.SearchKeyword = "some search";

        // Need to wait for debounce to avoid interference
        await Task.Delay(400);

        await vm.ClearFiltersCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.SearchKeyword);
    }

    [Fact]
    public async Task ClearFiltersAsync_RefreshesVideoList()
    {
        SetupDefaultVideoRepo();

        var vm = CreateViewModel();
        await vm.ClearFiltersCommand.ExecuteAsync(null);

        _videoRepoMock.Verify(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()), Times.Once);
    }

    [Fact]
    public async Task ClearFiltersAsync_SetsStatusToReady()
    {
        SetupDefaultVideoRepo();

        var vm = CreateViewModel();
        await vm.ClearFiltersCommand.ExecuteAsync(null);

        Assert.Equal("就绪", vm.StatusText);
    }

    #endregion

    #region Refresh Tests

    [Fact]
    public async Task RefreshAsync_ReloadsVideoList()
    {
        SetupDefaultVideoRepo(count: 3, totalCount: 3);

        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        _videoRepoMock.Verify(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()), Times.Once);
        Assert.Equal(3, vm.VideoListVm.Videos.Count);
    }

    [Fact]
    public async Task RefreshAsync_UpdatesStatusText()
    {
        SetupDefaultVideoRepo();

        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("就绪", vm.StatusText);
    }

    [Fact]
    public async Task RefreshAsync_SyncsPaginationInfo()
    {
        SetupDefaultVideoRepo(count: 50, totalCount: 100);

        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains("100", vm.PaginationVm.PageInfoText);
    }

    #endregion

    #region Messenger Integration Tests

    [Fact]
    public async Task Receive_SortChangedMessage_SyncsToVideoListVmAndReloads()
    {
        SetupDefaultVideoRepo();

        var vm = CreateViewModel();

        // Send a sort changed message
        WeakReferenceMessenger.Default.Send(new SortChangedMessage(SortField.Duration, SortDirection.Ascending));

        // Allow async reload to complete
        await Task.Delay(200);

        Assert.Equal(SortField.Duration, vm.VideoListVm.SortField);
        Assert.Equal(SortDirection.Ascending, vm.VideoListVm.SortDirection);
    }

    [Fact]
    public async Task Receive_RefreshRequestedMessage_RefreshesVideoList()
    {
        SetupDefaultVideoRepo();

        var vm = CreateViewModel();

        WeakReferenceMessenger.Default.Send(new RefreshRequestedMessage());

        // Allow async refresh to complete
        await Task.Delay(200);

        _videoRepoMock.Verify(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()), Times.AtLeastOnce);
    }

    [Fact]
    public void Receive_BatchOperationCompletedMessage_UpdatesStatusText()
    {
        var vm = CreateViewModel();

        WeakReferenceMessenger.Default.Send(new BatchOperationCompletedMessage("BatchDelete", 10, 2));

        Assert.Contains("BatchDelete", vm.StatusText);
        Assert.Contains("10", vm.StatusText);
        Assert.Contains("2", vm.StatusText);
    }

    [Fact]
    public void Receive_PageChangedMessage_UpdatesStatusText()
    {
        var vm = CreateViewModel();

        WeakReferenceMessenger.Default.Send(new PageChangedMessage(3));

        Assert.Contains("3", vm.StatusText);
    }

    #endregion

    #region Sub-ViewModel Exposure Tests

    [Fact]
    public void PaginationVm_IsExposedAsPublicProperty()
    {
        var vm = CreateViewModel();
        Assert.NotNull(vm.PaginationVm);
        Assert.IsType<PaginationViewModel>(vm.PaginationVm);
    }

    [Fact]
    public void SortVm_IsExposedAsPublicProperty()
    {
        var vm = CreateViewModel();
        Assert.NotNull(vm.SortVm);
        Assert.IsType<SortViewModel>(vm.SortVm);
    }

    [Fact]
    public void BatchOperationVm_IsExposedAsPublicProperty()
    {
        var vm = CreateViewModel();
        Assert.NotNull(vm.BatchOperationVm);
        Assert.IsType<BatchOperationViewModel>(vm.BatchOperationVm);
    }

    #endregion

    #region StatusText Update Tests

    [Fact]
    public async Task StatusText_ShowsRefreshingDuringRefresh()
    {
        var statusChanges = new List<string>();
        var tcs = new TaskCompletionSource<PagedResult<VideoEntry>>();

        _videoRepoMock
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StatusText))
                statusChanges.Add(vm.StatusText);
        };

        var refreshTask = vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains("正在刷新...", statusChanges);

        tcs.SetResult(CreatePagedResult(5, 5, 1, 50));
        await refreshTask;

        Assert.Equal("就绪", vm.StatusText);
    }

    [Fact]
    public void StatusText_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StatusText))
                raised = true;
        };

        vm.StatusText = "新状态";

        Assert.True(raised);
    }

    #endregion

    #region PropertyChanged Tests

    [Fact]
    public void SearchKeyword_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SearchKeyword))
                raised = true;
        };

        vm.SearchKeyword = "new keyword";

        Assert.True(raised);
    }

    #endregion

    #region FileWatcher Integration Tests

    [Fact]
    public void FileDeleted_MarksMatchingVideoAsFileMissing()
    {
        SetupDefaultVideoRepo(count: 3, totalCount: 3);

        var vm = CreateViewModel();

        // Manually add videos to the list to simulate loaded state
        var video = new VideoEntry { Id = 1, Title = "Test", FilePath = "/test/videos/video1.mp4" };
        vm.VideoListVm.Videos.Add(video);

        // Raise the FileDeleted event
        _fileWatcherMock.Raise(f => f.FileDeleted += null, _fileWatcherMock.Object, new FileDeletedEventArgs("/test/videos/video1.mp4"));

        Assert.True(video.IsFileMissing);
    }

    [Fact]
    public void FileDeleted_DoesNotAffectNonMatchingVideos()
    {
        var vm = CreateViewModel();

        var video1 = new VideoEntry { Id = 1, Title = "Test1", FilePath = "/test/videos/video1.mp4" };
        var video2 = new VideoEntry { Id = 2, Title = "Test2", FilePath = "/test/videos/video2.mp4" };
        vm.VideoListVm.Videos.Add(video1);
        vm.VideoListVm.Videos.Add(video2);

        _fileWatcherMock.Raise(f => f.FileDeleted += null, _fileWatcherMock.Object, new FileDeletedEventArgs("/test/videos/video1.mp4"));

        Assert.True(video1.IsFileMissing);
        Assert.False(video2.IsFileMissing);
    }

    [Fact]
    public void FileDeleted_NoMatchingVideo_DoesNotThrow()
    {
        var vm = CreateViewModel();

        var video = new VideoEntry { Id = 1, Title = "Test", FilePath = "/test/videos/video1.mp4" };
        vm.VideoListVm.Videos.Add(video);

        // Delete a file that doesn't match any video
        _fileWatcherMock.Raise(f => f.FileDeleted += null, _fileWatcherMock.Object, new FileDeletedEventArgs("/test/videos/nonexistent.mp4"));

        Assert.False(video.IsFileMissing);
    }

    [Fact]
    public void FileRenamed_UpdatesMatchingVideoFilePath()
    {
        var vm = CreateViewModel();

        var video = new VideoEntry { Id = 1, Title = "Test", FilePath = "/test/videos/old_name.mp4" };
        vm.VideoListVm.Videos.Add(video);

        _fileWatcherMock.Raise(f => f.FileRenamed += null, _fileWatcherMock.Object, new FileRenamedEventArgs("/test/videos/old_name.mp4", "/test/videos/new_name.mp4"));

        Assert.Equal("/test/videos/new_name.mp4", video.FilePath);
    }

    [Fact]
    public void FileRenamed_DoesNotAffectNonMatchingVideos()
    {
        var vm = CreateViewModel();

        var video1 = new VideoEntry { Id = 1, Title = "Test1", FilePath = "/test/videos/video1.mp4" };
        var video2 = new VideoEntry { Id = 2, Title = "Test2", FilePath = "/test/videos/video2.mp4" };
        vm.VideoListVm.Videos.Add(video1);
        vm.VideoListVm.Videos.Add(video2);

        _fileWatcherMock.Raise(f => f.FileRenamed += null, _fileWatcherMock.Object, new FileRenamedEventArgs("/test/videos/video1.mp4", "/test/videos/renamed.mp4"));

        Assert.Equal("/test/videos/renamed.mp4", video1.FilePath);
        Assert.Equal("/test/videos/video2.mp4", video2.FilePath);
    }

    [Fact]
    public void FileRenamed_NoMatchingVideo_DoesNotThrow()
    {
        var vm = CreateViewModel();

        var video = new VideoEntry { Id = 1, Title = "Test", FilePath = "/test/videos/video1.mp4" };
        vm.VideoListVm.Videos.Add(video);

        _fileWatcherMock.Raise(f => f.FileRenamed += null, _fileWatcherMock.Object, new FileRenamedEventArgs("/test/videos/nonexistent.mp4", "/test/videos/renamed.mp4"));

        Assert.Equal("/test/videos/video1.mp4", video.FilePath);
    }

    [Fact]
    public void FileDeleted_CaseInsensitiveMatch()
    {
        var vm = CreateViewModel();

        var video = new VideoEntry { Id = 1, Title = "Test", FilePath = "/Test/Videos/Video1.mp4" };
        vm.VideoListVm.Videos.Add(video);

        _fileWatcherMock.Raise(f => f.FileDeleted += null, _fileWatcherMock.Object, new FileDeletedEventArgs("/test/videos/video1.mp4"));

        Assert.True(video.IsFileMissing);
    }

    [Fact]
    public void FileRenamed_CaseInsensitiveMatch()
    {
        var vm = CreateViewModel();

        var video = new VideoEntry { Id = 1, Title = "Test", FilePath = "/Test/Videos/Old.mp4" };
        vm.VideoListVm.Videos.Add(video);

        _fileWatcherMock.Raise(f => f.FileRenamed += null, _fileWatcherMock.Object, new FileRenamedEventArgs("/test/videos/old.mp4", "/test/videos/new.mp4"));

        Assert.Equal("/test/videos/new.mp4", video.FilePath);
    }

    [Fact]
    public async Task InitializeAsync_StartsFileWatching()
    {
        SetupDefaultVideoRepo();
        SetupDefaultCategoryAndTags();

        var vm = CreateViewModel();
        await vm.InitializeCommand.ExecuteAsync(null);

        _fileWatcherMock.Verify(f => f.StartWatching("/test/videos"), Times.Once);
    }

    [Fact]
    public void Constructor_SubscribesToFileWatcherEvents()
    {
        // Verify that creating the ViewModel subscribes to events
        // by checking that raising events doesn't throw
        var vm = CreateViewModel();

        var video = new VideoEntry { Id = 1, Title = "Test", FilePath = "/test/video.mp4" };
        vm.VideoListVm.Videos.Add(video);

        // These should not throw - they should be handled by the subscriptions
        _fileWatcherMock.Raise(f => f.FileDeleted += null, _fileWatcherMock.Object, new FileDeletedEventArgs("/test/video.mp4"));
        Assert.True(video.IsFileMissing);
    }

    #endregion
}
