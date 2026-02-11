using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;
using VideoManager.ViewModels;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Tests that MainViewModel correctly delegates to INavigationService and IDialogService.
/// Since NavigationService and DialogService are WPF-dependent (they create windows/dialogs),
/// we test them through MainViewModel using mocks to verify interface contracts.
/// Requirements: 11.1, 11.2, 12.1, 12.2, 12.3, 12.4, 12.5
/// </summary>
public class NavigationDialogServiceTests
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

    public NavigationDialogServiceTests()
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

    private static VideoEntry CreateTestVideo(int id = 1, string title = "Test Video")
    {
        return new VideoEntry
        {
            Id = id,
            Title = title,
            FileName = $"video{id}.mp4",
            FilePath = $"/videos/video{id}.mp4",
            Duration = TimeSpan.FromMinutes(5),
            ImportedAt = DateTime.UtcNow
        };
    }

    private void SetupDefaultVideoRepo()
    {
        _videoRepoMock
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync((int page, int size, CancellationToken _, SortField __, SortDirection ___) =>
                new PagedResult<VideoEntry>(new List<VideoEntry>(), 0, page, size));
    }

    #region OpenVideoPlayerCommand Tests (Req 11.1)

    [Fact]
    public async Task OpenVideoPlayerCommand_WithSelectedVideo_CallsNavigationService()
    {
        // Arrange
        var video = CreateTestVideo();
        _navigationServiceMock
            .Setup(n => n.OpenVideoPlayerAsync(It.IsAny<VideoEntry>()))
            .Returns(Task.CompletedTask);

        var vm = CreateViewModel();
        vm.VideoListVm.Videos.Add(video);
        vm.VideoListVm.SelectedVideo = video;

        // Act
        await vm.OpenVideoPlayerCommand.ExecuteAsync(null);

        // Assert - Validates Req 11.1: NavigationService provides method to open video player
        _navigationServiceMock.Verify(
            n => n.OpenVideoPlayerAsync(It.Is<VideoEntry>(v => v.Id == video.Id)),
            Times.Once);
    }

    [Fact]
    public async Task OpenVideoPlayerCommand_WithNoSelectedVideo_DoesNotCallNavigationService()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.VideoListVm.SelectedVideo = null;

        // Act
        await vm.OpenVideoPlayerCommand.ExecuteAsync(null);

        // Assert
        _navigationServiceMock.Verify(
            n => n.OpenVideoPlayerAsync(It.IsAny<VideoEntry>()),
            Times.Never);
    }

    #endregion

    #region ImportVideosCommand Tests (Req 11.2)

    [Fact]
    public async Task ImportVideosCommand_CallsNavigationServiceOpenImportDialog()
    {
        // Arrange
        _navigationServiceMock
            .Setup(n => n.OpenImportDialogAsync())
            .ReturnsAsync((ImportResult?)null);

        var vm = CreateViewModel();

        // Act
        await vm.ImportVideosCommand.ExecuteAsync(null);

        // Assert - Validates Req 11.2: NavigationService provides method to open import dialog
        _navigationServiceMock.Verify(n => n.OpenImportDialogAsync(), Times.Once);
    }

    [Fact]
    public async Task ImportVideosCommand_WhenImportSucceeds_RefreshesVideoList()
    {
        // Arrange
        var importResult = new ImportResult(3, 0, new List<ImportError>());
        _navigationServiceMock
            .Setup(n => n.OpenImportDialogAsync())
            .ReturnsAsync(importResult);
        SetupDefaultVideoRepo();

        var vm = CreateViewModel();

        // Act
        await vm.ImportVideosCommand.ExecuteAsync(null);

        // Assert - After successful import, video list should be refreshed
        _videoRepoMock.Verify(
            r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportVideosCommand_WhenImportCancelled_DoesNotRefreshVideoList()
    {
        // Arrange
        _navigationServiceMock
            .Setup(n => n.OpenImportDialogAsync())
            .ReturnsAsync((ImportResult?)null);

        var vm = CreateViewModel();

        // Act
        await vm.ImportVideosCommand.ExecuteAsync(null);

        // Assert - When import is cancelled (null result), no refresh should happen
        _videoRepoMock.Verify(
            r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<SortField>(), It.IsAny<SortDirection>()),
            Times.Never);
    }

    #endregion

    #region EditVideoCommand Tests (Req 12.1)

    [Fact]
    public async Task EditVideoCommand_WithSelectedVideo_CallsDialogService()
    {
        // Arrange
        var video = CreateTestVideo();
        _dialogServiceMock
            .Setup(d => d.ShowEditDialogAsync(It.IsAny<VideoEntry>()))
            .ReturnsAsync(true);
        SetupDefaultVideoRepo();

        var vm = CreateViewModel();
        vm.VideoListVm.Videos.Add(video);
        vm.VideoListVm.SelectedVideo = video;

        // Act
        await vm.EditVideoCommand.ExecuteAsync(null);

        // Assert - Validates Req 12.1: DialogService provides edit dialog method
        _dialogServiceMock.Verify(
            d => d.ShowEditDialogAsync(It.Is<VideoEntry>(v => v.Id == video.Id)),
            Times.Once);
    }

    [Fact]
    public async Task EditVideoCommand_WithNoSelectedVideo_DoesNotCallDialogService()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.VideoListVm.SelectedVideo = null;

        // Act
        await vm.EditVideoCommand.ExecuteAsync(null);

        // Assert
        _dialogServiceMock.Verify(
            d => d.ShowEditDialogAsync(It.IsAny<VideoEntry>()),
            Times.Never);
    }

    #endregion

    #region DeleteVideoCommand Tests (Req 12.2)

    [Fact]
    public async Task DeleteVideoCommand_WithSelectedVideo_CallsDialogServiceShowDeleteConfirm()
    {
        // Arrange
        var video = CreateTestVideo(title: "My Video");
        _dialogServiceMock
            .Setup(d => d.ShowDeleteConfirmAsync(It.IsAny<string>()))
            .ReturnsAsync(((bool Confirmed, bool DeleteFile)?)(true, false));

        var mockDeleteService = new Mock<IDeleteService>();
        mockDeleteService
            .Setup(d => d.DeleteVideoAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteResult(true, null));

        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockScopedProvider = new Mock<IServiceProvider>();

        mockScopedProvider
            .Setup(sp => sp.GetService(typeof(IDeleteService)))
            .Returns(mockDeleteService.Object);
        mockScope
            .Setup(s => s.ServiceProvider)
            .Returns(mockScopedProvider.Object);
        mockScopeFactory
            .Setup(f => f.CreateScope())
            .Returns(mockScope.Object);
        _serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);

        SetupDefaultVideoRepo();

        var vm = CreateViewModel();
        vm.VideoListVm.Videos.Add(video);
        vm.VideoListVm.SelectedVideo = video;

        // Act
        await vm.DeleteVideoCommand.ExecuteAsync(null);

        // Assert - Validates Req 12.2: DialogService provides delete confirmation dialog
        _dialogServiceMock.Verify(
            d => d.ShowDeleteConfirmAsync(It.Is<string>(t => t == "My Video")),
            Times.Once);
    }

    [Fact]
    public async Task DeleteVideoCommand_WithNoSelectedVideo_DoesNotCallDialogService()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.VideoListVm.SelectedVideo = null;

        // Act
        await vm.DeleteVideoCommand.ExecuteAsync(null);

        // Assert
        _dialogServiceMock.Verify(
            d => d.ShowDeleteConfirmAsync(It.IsAny<string>()),
            Times.Never);
    }

    #endregion

    #region BatchDeleteCommand Tests (Req 12.2)

    [Fact]
    public async Task BatchDeleteCommand_CallsDialogServiceShowBatchDeleteConfirm()
    {
        // Arrange
        var video1 = CreateTestVideo(1, "Video 1");
        var video2 = CreateTestVideo(2, "Video 2");

        _dialogServiceMock
            .Setup(d => d.ShowBatchDeleteConfirmAsync(It.IsAny<int>()))
            .ReturnsAsync(((bool Confirmed, bool DeleteFile)?)(true, false));

        var mockDeleteService = new Mock<IDeleteService>();
        mockDeleteService
            .Setup(d => d.BatchDeleteAsync(It.IsAny<List<int>>(), It.IsAny<bool>(), It.IsAny<IProgress<BatchProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDeleteResult(2, 0, new List<DeleteError>()));

        var mockBackupService = new Mock<IBackupService>();
        mockBackupService
            .Setup(b => b.CreateBackupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("backup.db");

        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockScopedProvider = new Mock<IServiceProvider>();

        mockScopedProvider
            .Setup(sp => sp.GetService(typeof(IDeleteService)))
            .Returns(mockDeleteService.Object);
        mockScope
            .Setup(s => s.ServiceProvider)
            .Returns(mockScopedProvider.Object);
        mockScopeFactory
            .Setup(f => f.CreateScope())
            .Returns(mockScope.Object);
        _serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);
        _serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IBackupService)))
            .Returns(mockBackupService.Object);

        SetupDefaultVideoRepo();

        var vm = CreateViewModel();
        vm.VideoListVm.Videos.Add(video1);
        vm.VideoListVm.Videos.Add(video2);
        vm.VideoListVm.SelectedVideos.Add(video1);
        vm.VideoListVm.SelectedVideos.Add(video2);

        // Act
        await vm.BatchOperationVm.BatchDeleteCommand.ExecuteAsync(null);

        // Assert - Validates Req 12.2: DialogService provides batch delete confirmation
        _dialogServiceMock.Verify(
            d => d.ShowBatchDeleteConfirmAsync(It.Is<int>(count => count == 2)),
            Times.Once);
    }

    #endregion

    #region BatchTagCommand Tests (Req 12.3)

    [Fact]
    public async Task BatchTagCommand_CallsDialogServiceShowBatchTagDialog()
    {
        // Arrange
        var video1 = CreateTestVideo(1, "Video 1");
        var video2 = CreateTestVideo(2, "Video 2");
        var tag = new Tag { Id = 1, Name = "Action" };

        _dialogServiceMock
            .Setup(d => d.ShowBatchTagDialogAsync(It.IsAny<IEnumerable<Tag>>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Tag> { tag });

        var mockEditService = new Mock<IEditService>();
        mockEditService
            .Setup(e => e.BatchAddTagAsync(It.IsAny<List<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockScopedProvider = new Mock<IServiceProvider>();

        mockScopedProvider
            .Setup(sp => sp.GetService(typeof(IEditService)))
            .Returns(mockEditService.Object);
        mockScope
            .Setup(s => s.ServiceProvider)
            .Returns(mockScopedProvider.Object);
        mockScopeFactory
            .Setup(f => f.CreateScope())
            .Returns(mockScope.Object);
        _serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);

        SetupDefaultVideoRepo();

        // Need to set up tags on the CategoryViewModel
        _tagRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag> { tag });

        var vm = CreateViewModel();

        // Load tags first so CategoryVm.Tags is populated
        await vm.CategoryVm.LoadTagsCommand.ExecuteAsync(null);

        vm.VideoListVm.Videos.Add(video1);
        vm.VideoListVm.Videos.Add(video2);
        vm.VideoListVm.SelectedVideos.Add(video1);
        vm.VideoListVm.SelectedVideos.Add(video2);

        // Act
        await vm.BatchOperationVm.BatchTagCommand.ExecuteAsync(null);

        // Assert - Validates Req 12.3: DialogService provides batch tag dialog
        _dialogServiceMock.Verify(
            d => d.ShowBatchTagDialogAsync(It.IsAny<IEnumerable<Tag>>(), It.Is<int>(count => count == 2)),
            Times.Once);
    }

    #endregion

    #region BatchCategoryCommand Tests (Req 12.4)

    [Fact]
    public async Task BatchCategoryCommand_CallsDialogServiceShowBatchCategoryDialog()
    {
        // Arrange
        var video1 = CreateTestVideo(1, "Video 1");
        var video2 = CreateTestVideo(2, "Video 2");
        var category = new FolderCategory { Id = 1, Name = "Movies" };

        _dialogServiceMock
            .Setup(d => d.ShowBatchCategoryDialogAsync(It.IsAny<IEnumerable<FolderCategory>>(), It.IsAny<int>()))
            .ReturnsAsync(category);

        var mockEditService = new Mock<IEditService>();
        mockEditService
            .Setup(e => e.BatchMoveToCategoryAsync(It.IsAny<List<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockScopedProvider = new Mock<IServiceProvider>();

        mockScopedProvider
            .Setup(sp => sp.GetService(typeof(IEditService)))
            .Returns(mockEditService.Object);
        mockScope
            .Setup(s => s.ServiceProvider)
            .Returns(mockScopedProvider.Object);
        mockScopeFactory
            .Setup(f => f.CreateScope())
            .Returns(mockScope.Object);
        _serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);

        SetupDefaultVideoRepo();

        // Need to set up categories on the CategoryViewModel
        _categoryRepoMock
            .Setup(r => r.GetTreeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FolderCategory> { category });

        var vm = CreateViewModel();

        // Load categories first so CategoryVm.Categories is populated
        await vm.CategoryVm.LoadCategoriesCommand.ExecuteAsync(null);

        vm.VideoListVm.Videos.Add(video1);
        vm.VideoListVm.Videos.Add(video2);
        vm.VideoListVm.SelectedVideos.Add(video1);
        vm.VideoListVm.SelectedVideos.Add(video2);

        // Act
        await vm.BatchOperationVm.BatchCategoryCommand.ExecuteAsync(null);

        // Assert - Validates Req 12.4: DialogService provides batch category dialog
        _dialogServiceMock.Verify(
            d => d.ShowBatchCategoryDialogAsync(It.IsAny<IEnumerable<FolderCategory>>(), It.Is<int>(count => count == 2)),
            Times.Once);
    }

    #endregion

    #region No Selection Guard Tests

    [Fact]
    public async Task BatchDeleteCommand_WithNoSelection_DoesNotCallDialogService()
    {
        // Arrange
        var vm = CreateViewModel();
        // No videos selected

        // Act
        await vm.BatchOperationVm.BatchDeleteCommand.ExecuteAsync(null);

        // Assert
        _dialogServiceMock.Verify(
            d => d.ShowBatchDeleteConfirmAsync(It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task BatchTagCommand_WithNoSelection_DoesNotCallDialogService()
    {
        // Arrange
        var vm = CreateViewModel();
        // No videos selected

        // Act
        await vm.BatchOperationVm.BatchTagCommand.ExecuteAsync(null);

        // Assert
        _dialogServiceMock.Verify(
            d => d.ShowBatchTagDialogAsync(It.IsAny<IEnumerable<Tag>>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task BatchCategoryCommand_WithNoSelection_DoesNotCallDialogService()
    {
        // Arrange
        var vm = CreateViewModel();
        // No videos selected

        // Act
        await vm.BatchOperationVm.BatchCategoryCommand.ExecuteAsync(null);

        // Assert
        _dialogServiceMock.Verify(
            d => d.ShowBatchCategoryDialogAsync(It.IsAny<IEnumerable<FolderCategory>>(), It.IsAny<int>()),
            Times.Never);
    }

    #endregion
}
