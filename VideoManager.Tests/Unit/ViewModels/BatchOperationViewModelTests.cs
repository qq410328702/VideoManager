using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;
using VideoManager.ViewModels;
using Xunit;

namespace VideoManager.Tests.Unit.ViewModels;

[Collection("Messenger")]
public class BatchOperationViewModelTests : IDisposable
{
    private readonly Mock<IVideoRepository> _videoRepoMock;
    private readonly Mock<ICategoryRepository> _categoryRepoMock;
    private readonly Mock<ITagRepository> _tagRepoMock;
    private readonly Mock<IDialogService> _dialogServiceMock;
    private readonly Mock<IDeleteService> _deleteServiceMock;
    private readonly Mock<IBackupService> _backupServiceMock;
    private readonly Mock<IEditService> _editServiceMock;
    private readonly VideoListViewModel _videoListVm;
    private readonly CategoryViewModel _categoryVm;
    private readonly BatchOperationViewModel _sut;

    public BatchOperationViewModelTests()
    {
        WeakReferenceMessenger.Default.Reset();

        _videoRepoMock = new Mock<IVideoRepository>();
        _categoryRepoMock = new Mock<ICategoryRepository>();
        _tagRepoMock = new Mock<ITagRepository>();
        _dialogServiceMock = new Mock<IDialogService>();
        _deleteServiceMock = new Mock<IDeleteService>();
        _backupServiceMock = new Mock<IBackupService>();
        _editServiceMock = new Mock<IEditService>();

        _videoListVm = new VideoListViewModel(_videoRepoMock.Object);
        _categoryVm = new CategoryViewModel(_categoryRepoMock.Object, _tagRepoMock.Object);

        // Setup service provider with required services
        var services = new ServiceCollection();
        services.AddSingleton(_deleteServiceMock.Object);
        services.AddSingleton(_backupServiceMock.Object);
        services.AddSingleton(_editServiceMock.Object);
        var serviceProvider = services.BuildServiceProvider();

        _sut = new BatchOperationViewModel(_videoListVm, _categoryVm, _dialogServiceMock.Object, serviceProvider);
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.Reset();
    }

    private void SelectVideos(params int[] ids)
    {
        foreach (var id in ids)
        {
            _videoListVm.SelectedVideos.Add(new VideoEntry
            {
                Id = id,
                Title = $"Video {id}",
                FileName = $"v{id}.mp4",
                FilePath = $"/v{id}.mp4",
                ImportedAt = DateTime.UtcNow
            });
        }
    }

    [Fact]
    public async Task BatchDeleteAsync_SendsBatchOperationCompletedMessage()
    {
        SelectVideos(1, 2, 3);

        _dialogServiceMock
            .Setup(d => d.ShowBatchDeleteConfirmAsync(It.IsAny<int>()))
            .ReturnsAsync((true, false));

        _deleteServiceMock
            .Setup(d => d.BatchDeleteAsync(It.IsAny<List<int>>(), false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<int> ids, bool _, IProgress<(int, int)>? __, CancellationToken ___) =>
                new BatchDeleteResult(ids.Count, 0, new List<DeleteError>()));

        BatchOperationCompletedMessage? completedMsg = null;
        WeakReferenceMessenger.Default.Register<BatchOperationCompletedMessage>(this, (_, msg) => completedMsg = msg);

        await _sut.BatchDeleteCommand.ExecuteAsync(null);

        Assert.NotNull(completedMsg);
        Assert.Equal("BatchDelete", completedMsg.OperationType);
        Assert.Equal(3, completedMsg.SuccessCount);
        Assert.Equal(0, completedMsg.FailCount);
    }

    [Fact]
    public async Task BatchDeleteAsync_SendsRefreshRequestedMessage()
    {
        SelectVideos(1, 2);

        _dialogServiceMock
            .Setup(d => d.ShowBatchDeleteConfirmAsync(2))
            .ReturnsAsync((true, false));

        _deleteServiceMock
            .Setup(d => d.BatchDeleteAsync(It.IsAny<List<int>>(), false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDeleteResult(2, 0, new List<DeleteError>()));

        RefreshRequestedMessage? refreshMsg = null;
        WeakReferenceMessenger.Default.Register<RefreshRequestedMessage>(this, (_, msg) => refreshMsg = msg);

        await _sut.BatchDeleteCommand.ExecuteAsync(null);

        Assert.NotNull(refreshMsg);
    }

    [Fact]
    public async Task BatchDeleteAsync_NoSelection_DoesNotSendMessages()
    {
        // No videos selected
        BatchOperationCompletedMessage? completedMsg = null;
        RefreshRequestedMessage? refreshMsg = null;
        WeakReferenceMessenger.Default.Register<BatchOperationCompletedMessage>(this, (_, msg) => completedMsg = msg);
        WeakReferenceMessenger.Default.Register<RefreshRequestedMessage>(this, (_, msg) => refreshMsg = msg);

        await _sut.BatchDeleteCommand.ExecuteAsync(null);

        Assert.Null(completedMsg);
        Assert.Null(refreshMsg);
    }

    [Fact]
    public async Task BatchTagAsync_SendsBatchOperationCompletedMessage()
    {
        SelectVideos(1, 2);

        var tags = new List<Tag> { new() { Id = 10, Name = "TestTag" } };
        _categoryVm.Tags.Add(tags[0]);

        _dialogServiceMock
            .Setup(d => d.ShowBatchTagDialogAsync(It.IsAny<IEnumerable<Tag>>(), It.IsAny<int>()))
            .ReturnsAsync(tags);

        _editServiceMock
            .Setup(e => e.BatchAddTagAsync(It.IsAny<List<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        BatchOperationCompletedMessage? completedMsg = null;
        WeakReferenceMessenger.Default.Register<BatchOperationCompletedMessage>(this, (_, msg) => completedMsg = msg);

        await _sut.BatchTagCommand.ExecuteAsync(null);

        Assert.NotNull(completedMsg);
        Assert.Equal("BatchTag", completedMsg.OperationType);
        Assert.Equal(2, completedMsg.SuccessCount);
    }

    [Fact]
    public async Task BatchTagAsync_SendsRefreshRequestedMessage()
    {
        SelectVideos(1);

        var tags = new List<Tag> { new() { Id = 10, Name = "TestTag" } };
        _categoryVm.Tags.Add(tags[0]);

        _dialogServiceMock
            .Setup(d => d.ShowBatchTagDialogAsync(It.IsAny<IEnumerable<Tag>>(), It.IsAny<int>()))
            .ReturnsAsync(tags);

        _editServiceMock
            .Setup(e => e.BatchAddTagAsync(It.IsAny<List<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        RefreshRequestedMessage? refreshMsg = null;
        WeakReferenceMessenger.Default.Register<RefreshRequestedMessage>(this, (_, msg) => refreshMsg = msg);

        await _sut.BatchTagCommand.ExecuteAsync(null);

        Assert.NotNull(refreshMsg);
    }

    [Fact]
    public async Task BatchCategoryAsync_SendsBatchOperationCompletedMessage()
    {
        SelectVideos(1, 2, 3);

        var category = new FolderCategory { Id = 5, Name = "TestCategory" };
        _categoryVm.Categories.Add(category);

        _dialogServiceMock
            .Setup(d => d.ShowBatchCategoryDialogAsync(It.IsAny<IEnumerable<FolderCategory>>(), 3))
            .ReturnsAsync(category);

        _editServiceMock
            .Setup(e => e.BatchMoveToCategoryAsync(It.IsAny<List<int>>(), 5, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        BatchOperationCompletedMessage? completedMsg = null;
        WeakReferenceMessenger.Default.Register<BatchOperationCompletedMessage>(this, (_, msg) => completedMsg = msg);

        await _sut.BatchCategoryCommand.ExecuteAsync(null);

        Assert.NotNull(completedMsg);
        Assert.Equal("BatchCategory", completedMsg.OperationType);
        Assert.Equal(3, completedMsg.SuccessCount);
        Assert.Equal(0, completedMsg.FailCount);
    }

    [Fact]
    public async Task BatchCategoryAsync_SendsRefreshRequestedMessage()
    {
        SelectVideos(1);

        var category = new FolderCategory { Id = 5, Name = "TestCategory" };
        _categoryVm.Categories.Add(category);

        _dialogServiceMock
            .Setup(d => d.ShowBatchCategoryDialogAsync(It.IsAny<IEnumerable<FolderCategory>>(), 1))
            .ReturnsAsync(category);

        _editServiceMock
            .Setup(e => e.BatchMoveToCategoryAsync(It.IsAny<List<int>>(), 5, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        RefreshRequestedMessage? refreshMsg = null;
        WeakReferenceMessenger.Default.Register<RefreshRequestedMessage>(this, (_, msg) => refreshMsg = msg);

        await _sut.BatchCategoryCommand.ExecuteAsync(null);

        Assert.NotNull(refreshMsg);
    }

    [Fact]
    public async Task BatchDeleteAsync_WithFailures_ReportsCorrectCounts()
    {
        SelectVideos(1, 2, 3);

        _dialogServiceMock
            .Setup(d => d.ShowBatchDeleteConfirmAsync(3))
            .ReturnsAsync((true, false));

        _deleteServiceMock
            .Setup(d => d.BatchDeleteAsync(It.IsAny<List<int>>(), false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDeleteResult(2, 1, new List<DeleteError> { new(3, "File locked") }));

        BatchOperationCompletedMessage? completedMsg = null;
        WeakReferenceMessenger.Default.Register<BatchOperationCompletedMessage>(this, (_, msg) => completedMsg = msg);

        await _sut.BatchDeleteCommand.ExecuteAsync(null);

        Assert.NotNull(completedMsg);
        Assert.Equal(2, completedMsg.SuccessCount);
        Assert.Equal(1, completedMsg.FailCount);
    }
}
