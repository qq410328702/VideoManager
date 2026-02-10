using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;
using VideoManager.ViewModels;

namespace VideoManager.Tests.Unit.ViewModels;

public class EditViewModelTests
{
    private readonly Mock<IEditService> _editServiceMock;
    private readonly Mock<ITagRepository> _tagRepoMock;
    private readonly Mock<ICategoryRepository> _categoryRepoMock;

    public EditViewModelTests()
    {
        _editServiceMock = new Mock<IEditService>();
        _tagRepoMock = new Mock<ITagRepository>();
        _categoryRepoMock = new Mock<ICategoryRepository>();
        _categoryRepoMock.Setup(r => r.GetTreeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FolderCategory>());
    }

    private EditViewModel CreateViewModel()
    {
        return new EditViewModel(_editServiceMock.Object, _tagRepoMock.Object, _categoryRepoMock.Object);
    }

    private static VideoEntry CreateTestVideo(int id = 1, string title = "Test Video",
        string? description = "A test video", List<Tag>? tags = null)
    {
        return new VideoEntry
        {
            Id = id,
            Title = title,
            Description = description,
            FileName = "test.mp4",
            FilePath = "/videos/test.mp4",
            Tags = tags ?? new List<Tag>()
        };
    }

    private static List<Tag> CreateAllTags()
    {
        return new List<Tag>
        {
            new() { Id = 1, Name = "Action" },
            new() { Id = 2, Name = "Comedy" },
            new() { Id = 3, Name = "Drama" }
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullEditService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EditViewModel(null!, _tagRepoMock.Object, _categoryRepoMock.Object));
    }

    [Fact]
    public void Constructor_WithNullTagRepository_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EditViewModel(_editServiceMock.Object, null!, _categoryRepoMock.Object));
    }

    [Fact]
    public void Constructor_WithNullCategoryRepository_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EditViewModel(_editServiceMock.Object, _tagRepoMock.Object, null!));
    }

    [Fact]
    public void Constructor_InitializesDefaultValues()
    {
        var vm = CreateViewModel();

        Assert.Null(vm.Video);
        Assert.Equal(string.Empty, vm.Title);
        Assert.Null(vm.Description);
        Assert.NotNull(vm.VideoTags);
        Assert.Empty(vm.VideoTags);
        Assert.NotNull(vm.AvailableTags);
        Assert.Empty(vm.AvailableTags);
        Assert.Null(vm.SelectedAvailableTag);
        Assert.Null(vm.SelectedVideoTag);
        Assert.False(vm.IsSaving);
        Assert.False(vm.HasError);
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }

    #endregion

    #region LoadVideoAsync Tests

    [Fact]
    public async Task LoadVideoAsync_SetsVideoAndFields()
    {
        var video = CreateTestVideo();
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag>());

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(video);

        Assert.Equal(video, vm.Video);
        Assert.Equal("Test Video", vm.Title);
        Assert.Equal("A test video", vm.Description);
    }

    [Fact]
    public async Task LoadVideoAsync_PopulatesVideoTags()
    {
        var tags = new List<Tag>
        {
            new() { Id = 1, Name = "Action" },
            new() { Id = 2, Name = "Comedy" }
        };
        var video = CreateTestVideo(tags: tags);
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAllTags());

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(video);

        Assert.Equal(2, vm.VideoTags.Count);
        Assert.Contains(vm.VideoTags, t => t.Id == 1);
        Assert.Contains(vm.VideoTags, t => t.Id == 2);
    }

    [Fact]
    public async Task LoadVideoAsync_PopulatesAvailableTags_ExcludingVideoTags()
    {
        var videoTags = new List<Tag> { new() { Id = 1, Name = "Action" } };
        var video = CreateTestVideo(tags: videoTags);
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAllTags());

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(video);

        // Available tags should exclude the tag already on the video
        Assert.Equal(2, vm.AvailableTags.Count);
        Assert.DoesNotContain(vm.AvailableTags, t => t.Id == 1);
        Assert.Contains(vm.AvailableTags, t => t.Id == 2);
        Assert.Contains(vm.AvailableTags, t => t.Id == 3);
    }

    [Fact]
    public async Task LoadVideoAsync_ClearsErrorState()
    {
        var video = CreateTestVideo();
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag>());

        // First load a video so Video is not null, then trigger an error
        var initialVideo = CreateTestVideo(id: 99, title: "Initial");
        _editServiceMock
            .Setup(s => s.UpdateVideoInfoAsync(99, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Not found"));

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(initialVideo);

        // Trigger an error via save that throws
        vm.Title = "Valid Title";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);

        // Now load another video - error should be cleared
        await vm.LoadVideoAsync(video);
        Assert.False(vm.HasError);
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }

    [Fact]
    public async Task LoadVideoAsync_WithNullVideo_ThrowsArgumentNullException()
    {
        var vm = CreateViewModel();
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.LoadVideoAsync(null!));
    }

    #endregion

    #region SaveCommand Tests

    [Fact]
    public async Task SaveAsync_WithValidTitle_UpdatesVideoInfo()
    {
        var video = CreateTestVideo();
        var updatedVideo = CreateTestVideo(title: "Updated Title", description: "Updated Desc");
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag>());
        _editServiceMock
            .Setup(s => s.UpdateVideoInfoAsync(1, "Updated Title", "Updated Desc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedVideo);

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(video);

        vm.Title = "Updated Title";
        vm.Description = "Updated Desc";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Updated Title", vm.Title);
        Assert.Equal("Updated Desc", vm.Description);
        Assert.False(vm.HasError);
        _editServiceMock.Verify(
            s => s.UpdateVideoInfoAsync(1, "Updated Title", "Updated Desc", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveAsync_WithEmptyTitle_SetsError()
    {
        var video = CreateTestVideo();
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag>());

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(video);

        vm.Title = "";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Equal("标题不能为空", vm.ErrorMessage);
        _editServiceMock.Verify(
            s => s.UpdateVideoInfoAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveAsync_WithWhitespaceTitle_SetsError()
    {
        var video = CreateTestVideo();
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag>());

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(video);

        vm.Title = "   ";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Equal("标题不能为空", vm.ErrorMessage);
        _editServiceMock.Verify(
            s => s.UpdateVideoInfoAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveAsync_WhenServiceThrowsArgumentException_SetsError()
    {
        var video = CreateTestVideo();
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag>());
        _editServiceMock
            .Setup(s => s.UpdateVideoInfoAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Title cannot be empty."));

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(video);

        vm.Title = "Valid Title";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Equal("保存失败: Title cannot be empty.", vm.ErrorMessage);
    }

    [Fact]
    public async Task SaveAsync_WhenServiceThrowsKeyNotFoundException_SetsError()
    {
        var video = CreateTestVideo();
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag>());
        _editServiceMock
            .Setup(s => s.UpdateVideoInfoAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Video not found."));

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(video);

        vm.Title = "Valid Title";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Equal("保存失败: Video not found.", vm.ErrorMessage);
    }

    [Fact]
    public async Task SaveAsync_SetsIsSavingDuringOperation()
    {
        var video = CreateTestVideo();
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag>());

        bool wasSavingDuringCall = false;
        EditViewModel? capturedVm = null;

        _editServiceMock
            .Setup(s => s.UpdateVideoInfoAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<int, string, string?, CancellationToken>((id, title, desc, ct) =>
            {
                wasSavingDuringCall = capturedVm!.IsSaving;
                return Task.FromResult(CreateTestVideo(title: title, description: desc));
            });

        var vm = CreateViewModel();
        capturedVm = vm;
        await vm.LoadVideoAsync(video);

        vm.Title = "New Title";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(wasSavingDuringCall);
        Assert.False(vm.IsSaving);
    }

    [Fact]
    public async Task SaveAsync_TrimsTitle()
    {
        var video = CreateTestVideo();
        var updatedVideo = CreateTestVideo(title: "Trimmed Title");
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag>());
        _editServiceMock
            .Setup(s => s.UpdateVideoInfoAsync(1, "Trimmed Title", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedVideo);

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(video);

        vm.Title = "  Trimmed Title  ";
        vm.Description = null;
        await vm.SaveCommand.ExecuteAsync(null);

        _editServiceMock.Verify(
            s => s.UpdateVideoInfoAsync(1, "Trimmed Title", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveAsync_WithNullVideo_DoesNotCallService()
    {
        var vm = CreateViewModel();
        vm.Title = "Some Title";

        // Video is null, so save should be a no-op
        await vm.SaveCommand.ExecuteAsync(null);

        _editServiceMock.Verify(
            s => s.UpdateVideoInfoAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region AddTagCommand Tests

    [Fact]
    public async Task AddTagAsync_AddsTagToVideoAndMovesFromAvailable()
    {
        var video = CreateTestVideo();
        var allTags = CreateAllTags();
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(allTags);
        _editServiceMock
            .Setup(s => s.AddTagToVideoAsync(1, 1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(video);

        // Select a tag from available
        var tagToAdd = vm.AvailableTags.First(t => t.Id == 1);
        vm.SelectedAvailableTag = tagToAdd;

        await vm.AddTagCommand.ExecuteAsync(null);

        Assert.Contains(vm.VideoTags, t => t.Id == 1);
        Assert.DoesNotContain(vm.AvailableTags, t => t.Id == 1);
        Assert.Null(vm.SelectedAvailableTag);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task AddTagAsync_WhenServiceThrows_SetsError()
    {
        var video = CreateTestVideo();
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAllTags());
        _editServiceMock
            .Setup(s => s.AddTagToVideoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Tag not found."));

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(video);

        vm.SelectedAvailableTag = vm.AvailableTags.First();
        await vm.AddTagCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Equal("Tag not found.", vm.ErrorMessage);
    }

    [Fact]
    public void AddTagCommand_CannotExecute_WhenNoTagSelected()
    {
        var vm = CreateViewModel();
        vm.SelectedAvailableTag = null;

        Assert.False(vm.AddTagCommand.CanExecute(null));
    }

    [Fact]
    public void AddTagCommand_CanExecute_WhenTagSelected()
    {
        var vm = CreateViewModel();
        vm.SelectedAvailableTag = new Tag { Id = 1, Name = "Action" };

        Assert.True(vm.AddTagCommand.CanExecute(null));
    }

    #endregion

    #region RemoveTagCommand Tests

    [Fact]
    public async Task RemoveTagAsync_RemovesTagFromVideoAndMovesToAvailable()
    {
        var videoTags = new List<Tag> { new() { Id = 1, Name = "Action" } };
        var video = CreateTestVideo(tags: videoTags);
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAllTags());
        _editServiceMock
            .Setup(s => s.RemoveTagFromVideoAsync(1, 1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(video);

        var tagToRemove = vm.VideoTags.First(t => t.Id == 1);
        vm.SelectedVideoTag = tagToRemove;

        await vm.RemoveTagCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.VideoTags, t => t.Id == 1);
        Assert.Contains(vm.AvailableTags, t => t.Id == 1);
        Assert.Null(vm.SelectedVideoTag);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task RemoveTagAsync_WhenServiceThrows_SetsError()
    {
        var videoTags = new List<Tag> { new() { Id = 1, Name = "Action" } };
        var video = CreateTestVideo(tags: videoTags);
        _tagRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAllTags());
        _editServiceMock
            .Setup(s => s.RemoveTagFromVideoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Tag not found on video."));

        var vm = CreateViewModel();
        await vm.LoadVideoAsync(video);

        vm.SelectedVideoTag = vm.VideoTags.First();
        await vm.RemoveTagCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Equal("Tag not found on video.", vm.ErrorMessage);
    }

    [Fact]
    public void RemoveTagCommand_CannotExecute_WhenNoTagSelected()
    {
        var vm = CreateViewModel();
        vm.SelectedVideoTag = null;

        Assert.False(vm.RemoveTagCommand.CanExecute(null));
    }

    [Fact]
    public void RemoveTagCommand_CanExecute_WhenTagSelected()
    {
        var vm = CreateViewModel();
        vm.SelectedVideoTag = new Tag { Id = 1, Name = "Action" };

        Assert.True(vm.RemoveTagCommand.CanExecute(null));
    }

    #endregion

    #region SaveCommand CanExecute Tests

    [Fact]
    public void SaveCommand_CannotExecute_WhenTitleIsEmpty()
    {
        var vm = CreateViewModel();
        vm.Title = "";

        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void SaveCommand_CannotExecute_WhenTitleIsWhitespace()
    {
        var vm = CreateViewModel();
        vm.Title = "   ";

        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void SaveCommand_CanExecute_WhenTitleIsValid()
    {
        var vm = CreateViewModel();
        vm.Title = "Valid Title";

        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    #endregion
}
