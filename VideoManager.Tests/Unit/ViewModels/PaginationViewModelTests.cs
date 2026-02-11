using CommunityToolkit.Mvvm.Messaging;
using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.ViewModels;
using Xunit;

namespace VideoManager.Tests.Unit.ViewModels;

[Collection("Messenger")]
public class PaginationViewModelTests : IDisposable
{
    private readonly Mock<IVideoRepository> _videoRepoMock;
    private readonly VideoListViewModel _videoListVm;
    private readonly PaginationViewModel _sut;

    public PaginationViewModelTests()
    {
        WeakReferenceMessenger.Default.Reset();

        _videoRepoMock = new Mock<IVideoRepository>();
        _videoListVm = new VideoListViewModel(_videoRepoMock.Object);
        _sut = new PaginationViewModel(_videoListVm);
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.Reset();
    }

    private void SetupPagedRepo(int count, int totalCount, int pageSize = 50)
    {
        _videoRepoMock
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
                It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync((int page, int size, CancellationToken _, SortField __, SortDirection ___) =>
            {
                var items = Enumerable.Range(1, Math.Min(count, size)).Select(i => new VideoEntry
                {
                    Id = (page - 1) * size + i,
                    Title = $"Video {i}",
                    FileName = $"v{i}.mp4",
                    FilePath = $"/v{i}.mp4",
                    ImportedAt = DateTime.UtcNow
                }).ToList();
                return new PagedResult<VideoEntry>(items, totalCount, page, size);
            });
    }

    [Fact]
    public void DefaultValues_ShouldBeFirstPage()
    {
        Assert.Equal(1, _sut.CurrentPage);
        Assert.Equal(0, _sut.TotalPages);
        Assert.Contains("1", _sut.PageInfoText);
    }

    [Fact]
    public void SyncFromVideoListVm_SendsPageInfoUpdatedMessage()
    {
        PageInfoUpdatedMessage? received = null;
        WeakReferenceMessenger.Default.Register<PageInfoUpdatedMessage>(this, (_, msg) => received = msg);

        // Simulate VideoListViewModel state change
        _videoListVm.CurrentPage = 2;
        _videoListVm.TotalPages = 5;
        _videoListVm.TotalCount = 100;

        // SyncFromVideoListVm is called automatically via PropertyChanged subscription,
        // but we can also call it explicitly
        _sut.SyncFromVideoListVm();

        Assert.NotNull(received);
        Assert.Equal(2, received.CurrentPage);
        Assert.Equal(5, received.TotalPages);
        Assert.Equal(100, received.TotalCount);
    }

    [Fact]
    public async Task NextPageAsync_SendsPageChangedMessage()
    {
        // Setup: load videos so we have multiple pages
        SetupPagedRepo(count: 50, totalCount: 100);
        await _videoListVm.LoadVideosCommand.ExecuteAsync(null);
        _sut.SyncFromVideoListVm();

        PageChangedMessage? received = null;
        WeakReferenceMessenger.Default.Register<PageChangedMessage>(this, (_, msg) => received = msg);

        // Act: go to next page
        await _sut.NextPageCommand.ExecuteAsync(null);

        // Assert
        Assert.NotNull(received);
        Assert.Equal(2, received.NewPage);
    }

    [Fact]
    public async Task PreviousPageAsync_SendsPageChangedMessage()
    {
        // Setup: load videos so we have multiple pages
        SetupPagedRepo(count: 50, totalCount: 100);
        await _videoListVm.LoadVideosCommand.ExecuteAsync(null);

        // Navigate to page 2 via VideoListViewModel directly
        await _videoListVm.NextPageCommand.ExecuteAsync(null);
        _sut.SyncFromVideoListVm();

        // Verify we're on page 2
        Assert.Equal(2, _sut.CurrentPage);
        Assert.True(_sut.PreviousPageCommand.CanExecute(null));

        PageChangedMessage? received = null;
        WeakReferenceMessenger.Default.Register<PageChangedMessage>(this, (_, msg) => received = msg);

        // Act: go back to previous page
        await _sut.PreviousPageCommand.ExecuteAsync(null);

        // Assert
        Assert.NotNull(received);
        Assert.Equal(1, received.NewPage);
    }

    [Fact]
    public void SyncFromVideoListVm_UpdatesPageInfoText()
    {
        _videoListVm.CurrentPage = 3;
        _videoListVm.TotalPages = 10;
        _videoListVm.TotalCount = 500;

        _sut.SyncFromVideoListVm();

        Assert.Contains("3", _sut.PageInfoText);
        Assert.Contains("10", _sut.PageInfoText);
        Assert.Contains("500", _sut.PageInfoText);
    }

    [Fact]
    public void VideoListVmPropertyChanged_AutoSyncsAndSendsPageInfoUpdatedMessage()
    {
        var messages = new List<PageInfoUpdatedMessage>();
        WeakReferenceMessenger.Default.Register<PageInfoUpdatedMessage>(this, (_, msg) => messages.Add(msg));

        // Changing VideoListViewModel properties should auto-trigger sync via PropertyChanged
        _videoListVm.TotalCount = 200;

        Assert.True(messages.Count > 0);
        Assert.Equal(200, messages.Last().TotalCount);
    }

    [Fact]
    public void NextPageCommand_CannotExecute_WhenOnLastPage()
    {
        _videoListVm.CurrentPage = 1;
        _videoListVm.TotalPages = 1;
        _sut.SyncFromVideoListVm();

        Assert.False(_sut.NextPageCommand.CanExecute(null));
    }

    [Fact]
    public void PreviousPageCommand_CannotExecute_WhenOnFirstPage()
    {
        _videoListVm.CurrentPage = 1;
        _sut.SyncFromVideoListVm();

        Assert.False(_sut.PreviousPageCommand.CanExecute(null));
    }
}
