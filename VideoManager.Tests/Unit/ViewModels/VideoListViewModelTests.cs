using System.IO;
using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;
using VideoManager.ViewModels;

namespace VideoManager.Tests.Unit.ViewModels;

public class VideoListViewModelTests
{
    private readonly Mock<IVideoRepository> _repoMock;

    public VideoListViewModelTests()
    {
        _repoMock = new Mock<IVideoRepository>();
    }

    private VideoListViewModel CreateViewModel(IThumbnailPriorityLoader? loader = null)
    {
        return new VideoListViewModel(_repoMock.Object, loader);
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
        Assert.True(vm.IsLoading);
        tcs.SetResult(CreatePagedResult(0, 0, 1, 50));
        await loadTask;
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LoadVideosAsync_ClearsExistingVideosBeforeLoading()
    {
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(3, 3, 1, 50));
        var vm = CreateViewModel();
        await vm.LoadVideosCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Videos.Count);
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
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(50, 100, 1, 50));
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
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(50, 100, 1, 50));
        _repoMock.Setup(r => r.GetPagedAsync(2, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(50, 100, 2, 50));
        var vm = CreateViewModel();
        await vm.LoadVideosCommand.ExecuteAsync(null);
        await vm.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.CurrentPage);
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
            if (e.PropertyName == nameof(vm.SelectedVideo)) raised = true;
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
    public async Task LoadVideosAsync_WithThumbnailPriorityLoader_EnqueuesAllVideos()
    {
        var enqueuedItems = new List<(int videoId, string path, bool isVisible)>();
        var loaderMock = new Mock<IThumbnailPriorityLoader>();
        loaderMock.Setup(l => l.Enqueue(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<int, string, bool>((id, path, visible) => enqueuedItems.Add((id, path, visible)));
        var videos = new List<VideoEntry>
        {
            new() { Id = 1, Title = "V1", ThumbnailPath = "/thumbs/t1.jpg" },
            new() { Id = 2, Title = "V2", ThumbnailPath = "/thumbs/t2.jpg" },
        };
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(new PagedResult<VideoEntry>(videos, 2, 1, 50));
        var vm = new VideoListViewModel(_repoMock.Object, loaderMock.Object);
        await vm.LoadVideosCommand.ExecuteAsync(null);
        Assert.Equal(2, enqueuedItems.Count);
        Assert.Contains(enqueuedItems, e => e.videoId == 1 && e.path == "/thumbs/t1.jpg" && e.isVisible);
        Assert.Contains(enqueuedItems, e => e.videoId == 2 && e.path == "/thumbs/t2.jpg" && e.isVisible);
    }

    [Fact]
    public async Task LoadVideosAsync_WithoutThumbnailLoader_DoesNotThrow()
    {
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(CreatePagedResult(3, 3, 1, 50));
        var vm = CreateViewModel();
        await vm.LoadVideosCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Videos.Count);
    }

    [Fact]
    public async Task ThumbnailLoaded_Event_UpdatesVideoThumbnailPath()
    {
        Action<int, string?>? capturedHandler = null;
        var loaderMock = new Mock<IThumbnailPriorityLoader>();
        loaderMock.SetupAdd(l => l.ThumbnailLoaded += It.IsAny<Action<int, string?>>())
            .Callback<Action<int, string?>>(handler => capturedHandler = handler);
        var videos = new List<VideoEntry>
        {
            new() { Id = 1, Title = "V1", ThumbnailPath = "/thumbs/t1.jpg" },
        };
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(new PagedResult<VideoEntry>(videos, 1, 1, 50));
        var vm = new VideoListViewModel(_repoMock.Object, loaderMock.Object);
        await vm.LoadVideosCommand.ExecuteAsync(null);
        Assert.NotNull(capturedHandler);
        capturedHandler!.Invoke(1, "/resolved/t1.jpg");
        Assert.Equal("/resolved/t1.jpg", vm.Videos.First(v => v.Id == 1).ThumbnailPath);
    }

    [Fact]
    public async Task ThumbnailLoaded_Event_WithNullPath_DoesNotUpdateVideo()
    {
        Action<int, string?>? capturedHandler = null;
        var loaderMock = new Mock<IThumbnailPriorityLoader>();
        loaderMock.SetupAdd(l => l.ThumbnailLoaded += It.IsAny<Action<int, string?>>())
            .Callback<Action<int, string?>>(handler => capturedHandler = handler);
        var videos = new List<VideoEntry>
        {
            new() { Id = 1, Title = "V1", ThumbnailPath = "/thumbs/t1.jpg" },
        };
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(new PagedResult<VideoEntry>(videos, 1, 1, 50));
        var vm = new VideoListViewModel(_repoMock.Object, loaderMock.Object);
        await vm.LoadVideosCommand.ExecuteAsync(null);
        Assert.NotNull(capturedHandler);
        capturedHandler!.Invoke(1, null);
        Assert.Equal("/thumbs/t1.jpg", vm.Videos.First(v => v.Id == 1).ThumbnailPath);
    }

    [Fact]
    public async Task LoadVideosAsync_SkipsVideosWithNullThumbnailPath()
    {
        var enqueuedItems = new List<(int videoId, string path, bool isVisible)>();
        var loaderMock = new Mock<IThumbnailPriorityLoader>();
        loaderMock.Setup(l => l.Enqueue(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<int, string, bool>((id, path, visible) => enqueuedItems.Add((id, path, visible)));
        var videos = new List<VideoEntry>
        {
            new() { Id = 1, Title = "V1", ThumbnailPath = null },
            new() { Id = 2, Title = "V2", ThumbnailPath = "" },
            new() { Id = 3, Title = "V3", ThumbnailPath = "/thumbs/t3.jpg" },
        };
        _repoMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(new PagedResult<VideoEntry>(videos, 3, 1, 50));
        var vm = new VideoListViewModel(_repoMock.Object, loaderMock.Object);
        await vm.LoadVideosCommand.ExecuteAsync(null);
        Assert.Single(enqueuedItems);
        Assert.Equal(3, enqueuedItems[0].videoId);
        Assert.Equal("/thumbs/t3.jpg", enqueuedItems[0].path);
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
        Assert.Equal(5, vm.TotalPages);
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
        Assert.False(vm.LoadVideosCommand.CanExecute(null));
        tcs.SetResult(CreatePagedResult(0, 0, 1, 50));
        await loadTask;
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
    }

    [Fact]
    public void SelectedVideos_ThreeSelected_ShowsCorrectInfoText()
    {
        var vm = CreateViewModel();
        vm.SelectedVideos.Add(new VideoEntry { Id = 1, Title = "V1" });
        vm.SelectedVideos.Add(new VideoEntry { Id = 2, Title = "V2" });
        vm.SelectedVideos.Add(new VideoEntry { Id = 3, Title = "V3" });
        Assert.True(vm.HasMultipleSelection);
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
            if (e.PropertyName == nameof(vm.IsBatchOperating)) raised = true;
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
            if (e.PropertyName == nameof(vm.BatchProgressText)) raised = true;
        };
        vm.BatchProgressText = "Deleting... (2/5)";
        Assert.Equal("Deleting... (2/5)", vm.BatchProgressText);
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
        vm.BatchProgressText = "Deleting...";
        vm.BatchProgressPercentage = 50;
        vm.BatchEstimatedTimeRemaining = "30s remaining";
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
        vm.CancelBatchCommand.Execute(null);
        Assert.True(ct1.IsCancellationRequested);
        var ct2 = vm.BeginBatchOperation();
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
