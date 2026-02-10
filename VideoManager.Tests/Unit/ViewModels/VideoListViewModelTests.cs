using System.IO;
using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.ViewModels;

namespace VideoManager.Tests.Unit.ViewModels;

public class VideoListViewModelTests
{
    private readonly Mock<IVideoRepository> _repoMock;

    public VideoListViewModelTests()
    {
        _repoMock = new Mock<IVideoRepository>();
    }

    private VideoListViewModel CreateViewModel(Func<string, Task<string?>>? thumbnailLoader = null)
    {
        return new VideoListViewModel(_repoMock.Object, thumbnailLoader);
    }

    private static PagedResult<VideoEntry> CreatePagedResult(int count, int totalCount, int page, int pageSize)
    {
        var items = Enumerable.Range(1, count).Select(i => new VideoEntry
        {
            Id = (page - 1) * pageSize + i,
            Title = $"Video {(page - 1) * pageSize + i}",
            FileName = $"video{(page - 1) * pageSize + i}.mp4",
            FilePath = $"/videos/video{(page - 1) * pageSize + i}.mp4",
            ThumbnailPath = $"/thumbs/thumb{(page - 1) * pageSize + i}.jpg",
            Duration = TimeSpan.FromMinutes(i),
            ImportedAt = DateTime.UtcNow
        }).ToList();

        return new PagedResult<VideoEntry>(items, totalCount, page, pageSize);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullRepository_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new VideoListViewModel(null!));
    }

    [Fact]
    public void Constructor_InitializesDefaultValues()
    {
        var vm = CreateViewModel();

        Assert.NotNull(vm.Videos);
        Assert.Empty(vm.Videos);
        Assert.Equal(1, vm.CurrentPage);
        Assert.Equal(0, vm.TotalPages);
        Assert.Equal(50, vm.PageSize);
        Assert.Equal(0, vm.TotalCount);
        Assert.Null(vm.SelectedVideo);
        Assert.False(vm.IsLoading);
    }

    #endregion

    #region LoadVideos Tests

    [Fact]
    public async Task LoadVideosAsync_LoadsFirstPage()
    {
        var pagedResult = CreatePagedResult(count: 5, totalCount: 5, page: 1, pageSize: 50);
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(pagedResult);

        var vm = CreateViewModel();
        await vm.LoadVideosCommand.ExecuteAsync(null);

        Assert.Equal(5, vm.Videos.Count);
        Assert.Equal(5, vm.TotalCount);
        Assert.Equal(1, vm.TotalPages);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LoadVideosAsync_SetsIsLoadingDuringExecution()
    {
        var tcs = new TaskCompletionSource<PagedResult<VideoEntry>>();
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        var loadTask = vm.LoadVideosCommand.ExecuteAsync(null);

        // IsLoading should be true while loading
        Assert.True(vm.IsLoading);

        // Complete the task
        tcs.SetResult(CreatePagedResult(0, 0, 1, 50));
        await loadTask;

        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LoadVideosAsync_ClearsExistingVideosBeforeLoading()
    {
        // First load
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(3, 3, 1, 50));

        var vm = CreateViewModel();
        await vm.LoadVideosCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Videos.Count);

        // Second load with different data
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(2, 2, 1, 50));

        await vm.LoadVideosCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Videos.Count);
    }

    [Fact]
    public async Task LoadVideosAsync_EmptyResult_ShowsEmptyList()
    {
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(new PagedResult<VideoEntry>(new List<VideoEntry>(), 0, 1, 50));

        var vm = CreateViewModel();
        await vm.LoadVideosCommand.ExecuteAsync(null);

        Assert.Empty(vm.Videos);
        Assert.Equal(0, vm.TotalCount);
        Assert.Equal(0, vm.TotalPages);
    }

    #endregion

    #region Pagination Tests

    [Fact]
    public async Task NextPageAsync_IncrementsPageAndLoads()
    {
        // Setup page 1
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(50, 100, 1, 50));
        // Setup page 2
        _repoMock.Setup(r => r.GetPagedAsync(2, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(50, 100, 2, 50));

        var vm = CreateViewModel();
        await vm.LoadVideosCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.CurrentPage);
        Assert.Equal(2, vm.TotalPages);

        await vm.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.CurrentPage);
        _repoMock.Verify(r => r.GetPagedAsync(2, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()), Times.Once);
    }

    [Fact]
    public async Task PreviousPageAsync_DecrementsPageAndLoads()
    {
        // Setup page 1
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(50, 100, 1, 50));
        // Setup page 2
        _repoMock.Setup(r => r.GetPagedAsync(2, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(50, 100, 2, 50));

        var vm = CreateViewModel();
        // Go to page 2 first
        await vm.LoadVideosCommand.ExecuteAsync(null);
        await vm.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.CurrentPage);

        // Go back to page 1
        await vm.PreviousPageCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.CurrentPage);
    }

    [Fact]
    public async Task NextPageCommand_CannotExecute_WhenOnLastPage()
    {
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(5, 5, 1, 50));

        var vm = CreateViewModel();
        await vm.LoadVideosCommand.ExecuteAsync(null);

        // Only 1 page, so NextPage should not be executable
        Assert.False(vm.NextPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task PreviousPageCommand_CannotExecute_WhenOnFirstPage()
    {
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(50, 100, 1, 50));

        var vm = CreateViewModel();
        await vm.LoadVideosCommand.ExecuteAsync(null);

        Assert.False(vm.PreviousPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task NextPageCommand_CanExecute_WhenNotOnLastPage()
    {
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(50, 100, 1, 50));

        var vm = CreateViewModel();
        await vm.LoadVideosCommand.ExecuteAsync(null);

        Assert.True(vm.NextPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task GoToPageAsync_NavigatesToSpecificPage()
    {
        _repoMock.Setup(r => r.GetPagedAsync(It.IsAny<int>(), 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync((int page, int size, CancellationToken _, SortField __, SortDirection ___) =>
                CreatePagedResult(50, 200, page, size));

        var vm = CreateViewModel();
        await vm.LoadVideosCommand.ExecuteAsync(null);

        await vm.GoToPageAsync(3);

        Assert.Equal(3, vm.CurrentPage);
        _repoMock.Verify(r => r.GetPagedAsync(3, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()), Times.Once);
    }

    [Fact]
    public async Task GoToPageAsync_ClampsToMinimumPage()
    {
        _repoMock.Setup(r => r.GetPagedAsync(It.IsAny<int>(), 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync((int page, int size, CancellationToken _, SortField __, SortDirection ___) =>
                CreatePagedResult(50, 200, page, size));

        var vm = CreateViewModel();
        await vm.LoadVideosCommand.ExecuteAsync(null);

        await vm.GoToPageAsync(-5);

        Assert.Equal(1, vm.CurrentPage);
    }

    [Fact]
    public async Task GoToPageAsync_ClampsToMaximumPage()
    {
        _repoMock.Setup(r => r.GetPagedAsync(It.IsAny<int>(), 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync((int page, int size, CancellationToken _, SortField __, SortDirection ___) =>
                CreatePagedResult(50, 200, page, size));

        var vm = CreateViewModel();
        await vm.LoadVideosCommand.ExecuteAsync(null);
        // TotalPages should be 4 (200/50)

        await vm.GoToPageAsync(100);

        Assert.Equal(4, vm.CurrentPage);
    }

    #endregion

    #region CalculateTotalPages Tests

    [Theory]
    [InlineData(0, 50, 0)]
    [InlineData(1, 50, 1)]
    [InlineData(50, 50, 1)]
    [InlineData(51, 50, 2)]
    [InlineData(100, 50, 2)]
    [InlineData(101, 50, 3)]
    [InlineData(10000, 50, 200)]
    public void CalculateTotalPages_ReturnsCorrectValue(int totalCount, int pageSize, int expected)
    {
        Assert.Equal(expected, VideoListViewModel.CalculateTotalPages(totalCount, pageSize));
    }

    [Fact]
    public void CalculateTotalPages_ZeroPageSize_ReturnsZero()
    {
        Assert.Equal(0, VideoListViewModel.CalculateTotalPages(100, 0));
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

    #region Thumbnail Loading Tests

    [Fact]
    public async Task LoadVideosAsync_WithThumbnailLoader_InvokesThumbnailLoader()
    {
        var loadedPaths = new List<string>();
        Func<string, Task<string?>> thumbnailLoader = async (path) =>
        {
            loadedPaths.Add(path);
            await Task.Delay(1); // Simulate async work
            return path;
        };

        var videos = new List<VideoEntry>
        {
            new() { Id = 1, Title = "V1", ThumbnailPath = "/thumbs/t1.jpg" },
            new() { Id = 2, Title = "V2", ThumbnailPath = "/thumbs/t2.jpg" },
        };
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(new PagedResult<VideoEntry>(videos, 2, 1, 50));

        var vm = new VideoListViewModel(_repoMock.Object, thumbnailLoader);
        await vm.LoadVideosCommand.ExecuteAsync(null);

        // Give thumbnail loading a moment to complete (it's fire-and-forget)
        // Use a retry loop to handle scheduling delays in loaded test environments
        for (int i = 0; i < 20 && loadedPaths.Count < 2; i++)
        {
            await Task.Delay(100);
        }

        Assert.Contains("/thumbs/t1.jpg", loadedPaths);
        Assert.Contains("/thumbs/t2.jpg", loadedPaths);
    }

    [Fact]
    public async Task LoadVideosAsync_WithoutThumbnailLoader_DoesNotThrow()
    {
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(3, 3, 1, 50));

        var vm = CreateViewModel(); // No thumbnail loader
        await vm.LoadVideosCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Videos.Count);
    }

    [Fact]
    public async Task LoadVideosAsync_ThumbnailLoaderFailure_DoesNotAffectVideoLoading()
    {
        Func<string, Task<string?>> failingLoader = (_) =>
            throw new IOException("Thumbnail not found");

        var videos = new List<VideoEntry>
        {
            new() { Id = 1, Title = "V1", ThumbnailPath = "/thumbs/t1.jpg" },
        };
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(new PagedResult<VideoEntry>(videos, 1, 1, 50));

        var vm = new VideoListViewModel(_repoMock.Object, failingLoader);
        await vm.LoadVideosCommand.ExecuteAsync(null);

        // Give thumbnail loading a moment
        await Task.Delay(100);

        // Videos should still be loaded despite thumbnail failure
        Assert.Single(vm.Videos);
    }

    [Fact]
    public async Task LoadVideosAsync_SkipsVideosWithNullThumbnailPath()
    {
        var loadedPaths = new List<string>();
        Func<string, Task<string?>> thumbnailLoader = async (path) =>
        {
            loadedPaths.Add(path);
            await Task.Delay(1);
            return path;
        };

        var videos = new List<VideoEntry>
        {
            new() { Id = 1, Title = "V1", ThumbnailPath = null },
            new() { Id = 2, Title = "V2", ThumbnailPath = "" },
            new() { Id = 3, Title = "V3", ThumbnailPath = "/thumbs/t3.jpg" },
        };
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(new PagedResult<VideoEntry>(videos, 3, 1, 50));

        var vm = new VideoListViewModel(_repoMock.Object, thumbnailLoader);
        await vm.LoadVideosCommand.ExecuteAsync(null);

        await Task.Delay(100);

        // Only the video with a valid thumbnail path should be loaded
        Assert.Single(loadedPaths);
        Assert.Contains("/thumbs/t3.jpg", loadedPaths);
    }

    #endregion

    #region PageSize Tests

    [Fact]
    public async Task LoadVideosAsync_UsesConfiguredPageSize()
    {
        _repoMock.Setup(r => r.GetPagedAsync(1, 20, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(20, 100, 1, 20));

        var vm = CreateViewModel();
        vm.PageSize = 20;
        await vm.LoadVideosCommand.ExecuteAsync(null);

        _repoMock.Verify(r => r.GetPagedAsync(1, 20, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()), Times.Once);
        Assert.Equal(5, vm.TotalPages); // 100 / 20 = 5
    }

    #endregion

    #region IsLoading Guard Tests

    [Fact]
    public async Task LoadVideosCommand_CannotExecute_WhileLoading()
    {
        var tcs = new TaskCompletionSource<PagedResult<VideoEntry>>();
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        var loadTask = vm.LoadVideosCommand.ExecuteAsync(null);

        // While loading, CanExecute should be false
        Assert.False(vm.LoadVideosCommand.CanExecute(null));

        tcs.SetResult(CreatePagedResult(0, 0, 1, 50));
        await loadTask;

        // After loading, CanExecute should be true again
        Assert.True(vm.LoadVideosCommand.CanExecute(null));
    }

    #endregion

    #region Multi-Selection and Batch UI Tests

    [Fact]
    public void SelectedVideos_InitiallyEmpty()
    {
        var vm = CreateViewModel();

        Assert.NotNull(vm.SelectedVideos);
        Assert.Empty(vm.SelectedVideos);
        Assert.False(vm.HasMultipleSelection);
        Assert.Equal(string.Empty, vm.SelectionInfoText);
    }

    [Fact]
    public void SelectedVideos_SingleSelection_HasMultipleSelectionIsFalse()
    {
        var vm = CreateViewModel();
        vm.SelectedVideos.Add(new VideoEntry { Id = 1, Title = "Video 1" });

        Assert.Single(vm.SelectedVideos);
        Assert.False(vm.HasMultipleSelection);
        Assert.Equal(string.Empty, vm.SelectionInfoText);
    }

    [Fact]
    public void SelectedVideos_MultipleSelection_HasMultipleSelectionIsTrue()
    {
        var vm = CreateViewModel();
        vm.SelectedVideos.Add(new VideoEntry { Id = 1, Title = "Video 1" });
        vm.SelectedVideos.Add(new VideoEntry { Id = 2, Title = "Video 2" });

        Assert.Equal(2, vm.SelectedVideos.Count);
        Assert.True(vm.HasMultipleSelection);
        Assert.Equal("已选择 2 个视频", vm.SelectionInfoText);
    }

    [Fact]
    public void SelectedVideos_ThreeSelected_ShowsCorrectInfoText()
    {
        var vm = CreateViewModel();
        vm.SelectedVideos.Add(new VideoEntry { Id = 1, Title = "V1" });
        vm.SelectedVideos.Add(new VideoEntry { Id = 2, Title = "V2" });
        vm.SelectedVideos.Add(new VideoEntry { Id = 3, Title = "V3" });

        Assert.True(vm.HasMultipleSelection);
        Assert.Equal("已选择 3 个视频", vm.SelectionInfoText);
    }

    [Fact]
    public void SelectedVideos_RemoveToSingle_HasMultipleSelectionBecomesFalse()
    {
        var vm = CreateViewModel();
        var video1 = new VideoEntry { Id = 1, Title = "V1" };
        var video2 = new VideoEntry { Id = 2, Title = "V2" };
        vm.SelectedVideos.Add(video1);
        vm.SelectedVideos.Add(video2);

        Assert.True(vm.HasMultipleSelection);

        vm.SelectedVideos.Remove(video2);

        Assert.False(vm.HasMultipleSelection);
        Assert.Equal(string.Empty, vm.SelectionInfoText);
    }

    [Fact]
    public void ClearSelectionCommand_ClearsAllSelectedVideos()
    {
        var vm = CreateViewModel();
        vm.SelectedVideos.Add(new VideoEntry { Id = 1, Title = "V1" });
        vm.SelectedVideos.Add(new VideoEntry { Id = 2, Title = "V2" });
        vm.SelectedVideos.Add(new VideoEntry { Id = 3, Title = "V3" });

        Assert.Equal(3, vm.SelectedVideos.Count);

        vm.ClearSelectionCommand.Execute(null);

        Assert.Empty(vm.SelectedVideos);
        Assert.False(vm.HasMultipleSelection);
        Assert.Equal(string.Empty, vm.SelectionInfoText);
    }

    [Fact]
    public void GetSelectedVideoIds_ReturnsCorrectIds()
    {
        var vm = CreateViewModel();
        vm.SelectedVideos.Add(new VideoEntry { Id = 10, Title = "V10" });
        vm.SelectedVideos.Add(new VideoEntry { Id = 20, Title = "V20" });
        vm.SelectedVideos.Add(new VideoEntry { Id = 30, Title = "V30" });

        var ids = vm.GetSelectedVideoIds();

        Assert.Equal(3, ids.Count);
        Assert.Contains(10, ids);
        Assert.Contains(20, ids);
        Assert.Contains(30, ids);
    }

    [Fact]
    public void GetSelectedVideoIds_EmptySelection_ReturnsEmptyList()
    {
        var vm = CreateViewModel();

        var ids = vm.GetSelectedVideoIds();

        Assert.NotNull(ids);
        Assert.Empty(ids);
    }

    [Fact]
    public void IsBatchOperating_DefaultIsFalse()
    {
        var vm = CreateViewModel();

        Assert.False(vm.IsBatchOperating);
        Assert.Equal(string.Empty, vm.BatchProgressText);
    }

    [Fact]
    public void IsBatchOperating_CanBeSetAndRaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsBatchOperating))
                raised = true;
        };

        vm.IsBatchOperating = true;

        Assert.True(vm.IsBatchOperating);
        Assert.True(raised);
    }

    [Fact]
    public void BatchProgressText_CanBeSetAndRaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.BatchProgressText))
                raised = true;
        };

        vm.BatchProgressText = "正在删除... (2/5)";

        Assert.Equal("正在删除... (2/5)", vm.BatchProgressText);
        Assert.True(raised);
    }

    [Fact]
    public async Task LoadVideosAsync_ClearsSelectedVideos()
    {
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(3, 3, 1, 50));

        var vm = CreateViewModel();
        vm.SelectedVideos.Add(new VideoEntry { Id = 99, Title = "Old Selection" });
        vm.SelectedVideos.Add(new VideoEntry { Id = 100, Title = "Old Selection 2" });

        Assert.True(vm.HasMultipleSelection);

        await vm.LoadVideosCommand.ExecuteAsync(null);

        Assert.Empty(vm.SelectedVideos);
        Assert.False(vm.HasMultipleSelection);
    }

    #endregion

    #region Batch Operation Cancel & Progress

    [Fact]
    public void BeginBatchOperation_SetsIsBatchOperatingAndReturnsCancellationToken()
    {
        var vm = CreateViewModel();

        var ct = vm.BeginBatchOperation();

        Assert.True(vm.IsBatchOperating);
        Assert.False(ct.IsCancellationRequested);
        Assert.Equal(string.Empty, vm.BatchProgressText);
        Assert.Equal(0, vm.BatchProgressPercentage);
        Assert.Equal(string.Empty, vm.BatchEstimatedTimeRemaining);

        vm.EndBatchOperation();
    }

    [Fact]
    public void EndBatchOperation_ResetsAllBatchProperties()
    {
        var vm = CreateViewModel();
        vm.BeginBatchOperation();
        vm.BatchProgressText = "正在删除...";
        vm.BatchProgressPercentage = 50;
        vm.BatchEstimatedTimeRemaining = "预计剩余 30 秒";

        vm.EndBatchOperation();

        Assert.False(vm.IsBatchOperating);
        Assert.Equal(string.Empty, vm.BatchProgressText);
        Assert.Equal(0, vm.BatchProgressPercentage);
        Assert.Equal(string.Empty, vm.BatchEstimatedTimeRemaining);
    }

    [Fact]
    public void CancelBatchCommand_CancelsTheCancellationToken()
    {
        var vm = CreateViewModel();
        var ct = vm.BeginBatchOperation();

        Assert.False(ct.IsCancellationRequested);

        vm.CancelBatchCommand.Execute(null);

        Assert.True(ct.IsCancellationRequested);

        vm.EndBatchOperation();
    }

    [Fact]
    public void CancelBatchCommand_CannotExecuteWhenNotBatchOperating()
    {
        var vm = CreateViewModel();

        Assert.False(vm.CancelBatchCommand.CanExecute(null));
    }

    [Fact]
    public void CancelBatchCommand_CanExecuteWhenBatchOperating()
    {
        var vm = CreateViewModel();
        vm.BeginBatchOperation();

        Assert.True(vm.CancelBatchCommand.CanExecute(null));

        vm.EndBatchOperation();
    }

    [Fact]
    public void BeginBatchOperation_DisposesOldCancellationTokenSource()
    {
        var vm = CreateViewModel();

        var ct1 = vm.BeginBatchOperation();
        // Cancel the first token before beginning a new operation
        vm.CancelBatchCommand.Execute(null);
        Assert.True(ct1.IsCancellationRequested);

        var ct2 = vm.BeginBatchOperation();

        // The second token should be fresh and not cancelled
        Assert.False(ct2.IsCancellationRequested);

        vm.EndBatchOperation();
    }

    [Fact]
    public void BatchProgressPercentage_DefaultIsZero()
    {
        var vm = CreateViewModel();

        Assert.Equal(0, vm.BatchProgressPercentage);
    }

    [Fact]
    public void BatchEstimatedTimeRemaining_DefaultIsEmpty()
    {
        var vm = CreateViewModel();

        Assert.Equal(string.Empty, vm.BatchEstimatedTimeRemaining);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(30, "预计剩余 30 秒")]
    [InlineData(90, "预计剩余 1:30")]
    [InlineData(3661, "预计剩余 1:01:01")]
    public void FormatTimeRemaining_FormatsCorrectly(int? totalSeconds, string expected)
    {
        TimeSpan? ts = totalSeconds.HasValue ? TimeSpan.FromSeconds(totalSeconds.Value) : null;

        var result = VideoListViewModel.FormatTimeRemaining(ts);

        Assert.Equal(expected, result);
    }

    #endregion
}

