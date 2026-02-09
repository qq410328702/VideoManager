using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.ViewModels;

namespace VideoManager.Tests.Unit.ViewModels;

public class CategoryViewModelTests
{
    private readonly Mock<ICategoryRepository> _categoryRepoMock;
    private readonly Mock<ITagRepository> _tagRepoMock;

    public CategoryViewModelTests()
    {
        _categoryRepoMock = new Mock<ICategoryRepository>();
        _tagRepoMock = new Mock<ITagRepository>();
    }

    private CategoryViewModel CreateViewModel()
    {
        return new CategoryViewModel(_categoryRepoMock.Object, _tagRepoMock.Object);
    }

    private static List<FolderCategory> CreateCategoryTree()
    {
        var root1 = new FolderCategory
        {
            Id = 1,
            Name = "Movies",
            Children = new List<FolderCategory>
            {
                new() { Id = 3, Name = "Action", ParentId = 1, Children = new List<FolderCategory>() },
                new() { Id = 4, Name = "Comedy", ParentId = 1, Children = new List<FolderCategory>() }
            }
        };
        var root2 = new FolderCategory
        {
            Id = 2,
            Name = "Music",
            Children = new List<FolderCategory>()
        };
        return new List<FolderCategory> { root1, root2 };
    }

    private static List<Tag> CreateTagList()
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
    public void Constructor_WithNullCategoryRepository_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CategoryViewModel(null!, _tagRepoMock.Object));
    }

    [Fact]
    public void Constructor_WithNullTagRepository_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CategoryViewModel(_categoryRepoMock.Object, null!));
    }

    [Fact]
    public void Constructor_InitializesDefaultValues()
    {
        var vm = CreateViewModel();

        Assert.NotNull(vm.Categories);
        Assert.Empty(vm.Categories);
        Assert.NotNull(vm.Tags);
        Assert.Empty(vm.Tags);
        Assert.Null(vm.SelectedCategory);
        Assert.Null(vm.SelectedTag);
        Assert.Equal(string.Empty, vm.NewCategoryName);
        Assert.Equal(string.Empty, vm.NewTagName);
        Assert.False(vm.IsLoading);
    }

    #endregion

    #region LoadCategories Tests

    [Fact]
    public async Task LoadCategoriesAsync_PopulatesCategories()
    {
        var tree = CreateCategoryTree();
        _categoryRepoMock
            .Setup(r => r.GetTreeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tree);

        var vm = CreateViewModel();
        await vm.LoadCategoriesCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Categories.Count);
        Assert.Equal("Movies", vm.Categories[0].Name);
        Assert.Equal("Music", vm.Categories[1].Name);
        Assert.Equal(2, vm.Categories[0].Children.Count);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LoadCategoriesAsync_SetsIsLoadingDuringExecution()
    {
        var tcs = new TaskCompletionSource<List<FolderCategory>>();
        _categoryRepoMock
            .Setup(r => r.GetTreeAsync(It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        var loadTask = vm.LoadCategoriesCommand.ExecuteAsync(null);

        Assert.True(vm.IsLoading);

        tcs.SetResult(new List<FolderCategory>());
        await loadTask;

        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LoadCategoriesAsync_ClearsExistingCategoriesBeforeLoading()
    {
        _categoryRepoMock
            .Setup(r => r.GetTreeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCategoryTree());

        var vm = CreateViewModel();
        await vm.LoadCategoriesCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Categories.Count);

        // Second load with different data
        _categoryRepoMock
            .Setup(r => r.GetTreeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FolderCategory>
            {
                new() { Id = 10, Name = "New Root", Children = new List<FolderCategory>() }
            });

        await vm.LoadCategoriesCommand.ExecuteAsync(null);
        Assert.Single(vm.Categories);
        Assert.Equal("New Root", vm.Categories[0].Name);
    }

    [Fact]
    public async Task LoadCategoriesAsync_EmptyResult_ShowsEmptyList()
    {
        _categoryRepoMock
            .Setup(r => r.GetTreeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FolderCategory>());

        var vm = CreateViewModel();
        await vm.LoadCategoriesCommand.ExecuteAsync(null);

        Assert.Empty(vm.Categories);
    }

    #endregion

    #region LoadTags Tests

    [Fact]
    public async Task LoadTagsAsync_PopulatesTags()
    {
        var tags = CreateTagList();
        _tagRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);

        var vm = CreateViewModel();
        await vm.LoadTagsCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Tags.Count);
        Assert.Equal("Action", vm.Tags[0].Name);
        Assert.Equal("Comedy", vm.Tags[1].Name);
        Assert.Equal("Drama", vm.Tags[2].Name);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LoadTagsAsync_SetsIsLoadingDuringExecution()
    {
        var tcs = new TaskCompletionSource<List<Tag>>();
        _tagRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        var loadTask = vm.LoadTagsCommand.ExecuteAsync(null);

        Assert.True(vm.IsLoading);

        tcs.SetResult(new List<Tag>());
        await loadTask;

        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LoadTagsAsync_ClearsExistingTagsBeforeLoading()
    {
        _tagRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTagList());

        var vm = CreateViewModel();
        await vm.LoadTagsCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Tags.Count);

        // Second load with different data
        _tagRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag> { new() { Id = 10, Name = "NewTag" } });

        await vm.LoadTagsCommand.ExecuteAsync(null);
        Assert.Single(vm.Tags);
        Assert.Equal("NewTag", vm.Tags[0].Name);
    }

    [Fact]
    public async Task LoadTagsAsync_EmptyResult_ShowsEmptyList()
    {
        _tagRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag>());

        var vm = CreateViewModel();
        await vm.LoadTagsCommand.ExecuteAsync(null);

        Assert.Empty(vm.Tags);
    }

    #endregion

    #region AddCategory Tests

    [Fact]
    public async Task AddCategoryAsync_AddsRootCategory_WhenNoSelection()
    {
        var addedCategory = new FolderCategory { Id = 10, Name = "NewCategory", Children = new List<FolderCategory>() };
        _categoryRepoMock
            .Setup(r => r.AddAsync(It.IsAny<FolderCategory>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(addedCategory);

        var vm = CreateViewModel();
        vm.NewCategoryName = "NewCategory";

        await vm.AddCategoryCommand.ExecuteAsync(null);

        Assert.Single(vm.Categories);
        Assert.Equal("NewCategory", vm.Categories[0].Name);
        Assert.Equal(10, vm.Categories[0].Id);
        Assert.Equal(string.Empty, vm.NewCategoryName);
    }

    [Fact]
    public async Task AddCategoryAsync_AddsChildCategory_WhenCategorySelected()
    {
        var parentCategory = new FolderCategory
        {
            Id = 1,
            Name = "Parent",
            Children = new List<FolderCategory>()
        };

        var addedChild = new FolderCategory { Id = 10, Name = "Child", ParentId = 1, Children = new List<FolderCategory>() };
        _categoryRepoMock
            .Setup(r => r.AddAsync(
                It.Is<FolderCategory>(c => c.ParentId == 1),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(addedChild);

        var vm = CreateViewModel();
        vm.Categories.Add(parentCategory);
        vm.SelectedCategory = parentCategory;
        vm.NewCategoryName = "Child";

        await vm.AddCategoryCommand.ExecuteAsync(null);

        Assert.Single(parentCategory.Children);
        Assert.Equal("Child", parentCategory.Children.First().Name);
        Assert.Equal(string.Empty, vm.NewCategoryName);
    }

    [Fact]
    public async Task AddCategoryAsync_TrimsName()
    {
        _categoryRepoMock
            .Setup(r => r.AddAsync(
                It.Is<FolderCategory>(c => c.Name == "Trimmed"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FolderCategory { Id = 10, Name = "Trimmed", Children = new List<FolderCategory>() });

        var vm = CreateViewModel();
        vm.NewCategoryName = "  Trimmed  ";

        await vm.AddCategoryCommand.ExecuteAsync(null);

        _categoryRepoMock.Verify(r => r.AddAsync(
            It.Is<FolderCategory>(c => c.Name == "Trimmed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddCategoryAsync_SetsIsLoadingDuringExecution()
    {
        var tcs = new TaskCompletionSource<FolderCategory>();
        _categoryRepoMock
            .Setup(r => r.AddAsync(It.IsAny<FolderCategory>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        vm.NewCategoryName = "Test";

        var addTask = vm.AddCategoryCommand.ExecuteAsync(null);

        Assert.True(vm.IsLoading);

        tcs.SetResult(new FolderCategory { Id = 1, Name = "Test", Children = new List<FolderCategory>() });
        await addTask;

        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task AddCategoryAsync_PassesParentIdNull_WhenNoSelection()
    {
        _categoryRepoMock
            .Setup(r => r.AddAsync(It.IsAny<FolderCategory>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FolderCategory c, CancellationToken _) =>
            {
                c.Id = 10;
                return c;
            });

        var vm = CreateViewModel();
        vm.NewCategoryName = "Root";

        await vm.AddCategoryCommand.ExecuteAsync(null);

        _categoryRepoMock.Verify(r => r.AddAsync(
            It.Is<FolderCategory>(c => c.ParentId == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region AddCategoryCommand CanExecute Tests

    [Fact]
    public void AddCategoryCommand_CannotExecute_WhenNameEmpty()
    {
        var vm = CreateViewModel();
        vm.NewCategoryName = "";

        Assert.False(vm.AddCategoryCommand.CanExecute(null));
    }

    [Fact]
    public void AddCategoryCommand_CannotExecute_WhenNameWhitespace()
    {
        var vm = CreateViewModel();
        vm.NewCategoryName = "   ";

        Assert.False(vm.AddCategoryCommand.CanExecute(null));
    }

    [Fact]
    public void AddCategoryCommand_CanExecute_WhenNameSet()
    {
        var vm = CreateViewModel();
        vm.NewCategoryName = "New Category";

        Assert.True(vm.AddCategoryCommand.CanExecute(null));
    }

    #endregion

    #region DeleteCategory Tests

    [Fact]
    public async Task DeleteCategoryAsync_RemovesRootCategory()
    {
        var category = new FolderCategory { Id = 1, Name = "ToDelete", Children = new List<FolderCategory>() };
        _categoryRepoMock
            .Setup(r => r.DeleteAsync(1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vm = CreateViewModel();
        vm.Categories.Add(category);
        vm.SelectedCategory = category;

        await vm.DeleteCategoryCommand.ExecuteAsync(null);

        Assert.Empty(vm.Categories);
        Assert.Null(vm.SelectedCategory);
    }

    [Fact]
    public async Task DeleteCategoryAsync_RemovesChildCategory()
    {
        var child = new FolderCategory { Id = 3, Name = "Child", ParentId = 1, Children = new List<FolderCategory>() };
        var parent = new FolderCategory
        {
            Id = 1,
            Name = "Parent",
            Children = new List<FolderCategory> { child }
        };

        _categoryRepoMock
            .Setup(r => r.DeleteAsync(3, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vm = CreateViewModel();
        vm.Categories.Add(parent);
        vm.SelectedCategory = child;

        await vm.DeleteCategoryCommand.ExecuteAsync(null);

        Assert.Empty(parent.Children);
        Assert.Null(vm.SelectedCategory);
    }

    [Fact]
    public async Task DeleteCategoryAsync_SetsIsLoadingDuringExecution()
    {
        var tcs = new TaskCompletionSource();
        _categoryRepoMock
            .Setup(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var category = new FolderCategory { Id = 1, Name = "Test", Children = new List<FolderCategory>() };
        var vm = CreateViewModel();
        vm.Categories.Add(category);
        vm.SelectedCategory = category;

        var deleteTask = vm.DeleteCategoryCommand.ExecuteAsync(null);

        Assert.True(vm.IsLoading);

        tcs.SetResult();
        await deleteTask;

        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task DeleteCategoryAsync_CallsRepositoryWithCorrectId()
    {
        var category = new FolderCategory { Id = 42, Name = "Test", Children = new List<FolderCategory>() };
        _categoryRepoMock
            .Setup(r => r.DeleteAsync(42, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vm = CreateViewModel();
        vm.Categories.Add(category);
        vm.SelectedCategory = category;

        await vm.DeleteCategoryCommand.ExecuteAsync(null);

        _categoryRepoMock.Verify(r => r.DeleteAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteCategoryCommand CanExecute Tests

    [Fact]
    public void DeleteCategoryCommand_CannotExecute_WhenNoCategorySelected()
    {
        var vm = CreateViewModel();

        Assert.False(vm.DeleteCategoryCommand.CanExecute(null));
    }

    [Fact]
    public void DeleteCategoryCommand_CanExecute_WhenCategorySelected()
    {
        var vm = CreateViewModel();
        vm.SelectedCategory = new FolderCategory { Id = 1, Name = "Test" };

        Assert.True(vm.DeleteCategoryCommand.CanExecute(null));
    }

    #endregion

    #region AddTag Tests (Requirement 3.1)

    [Fact]
    public async Task AddTagAsync_AddsNewTag()
    {
        var addedTag = new Tag { Id = 10, Name = "NewTag" };
        _tagRepoMock
            .Setup(r => r.ExistsByNameAsync("NewTag", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _tagRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(addedTag);

        var vm = CreateViewModel();
        vm.NewTagName = "NewTag";

        await vm.AddTagCommand.ExecuteAsync(null);

        Assert.Single(vm.Tags);
        Assert.Equal("NewTag", vm.Tags[0].Name);
        Assert.Equal(10, vm.Tags[0].Id);
        Assert.Equal(string.Empty, vm.NewTagName);
    }

    [Fact]
    public async Task AddTagAsync_DoesNotAdd_WhenNameAlreadyExists()
    {
        _tagRepoMock
            .Setup(r => r.ExistsByNameAsync("Existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var vm = CreateViewModel();
        vm.NewTagName = "Existing";

        await vm.AddTagCommand.ExecuteAsync(null);

        Assert.Empty(vm.Tags);
        _tagRepoMock.Verify(r => r.AddAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddTagAsync_TrimsName()
    {
        _tagRepoMock
            .Setup(r => r.ExistsByNameAsync("Trimmed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _tagRepoMock
            .Setup(r => r.AddAsync(
                It.Is<Tag>(t => t.Name == "Trimmed"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tag { Id = 10, Name = "Trimmed" });

        var vm = CreateViewModel();
        vm.NewTagName = "  Trimmed  ";

        await vm.AddTagCommand.ExecuteAsync(null);

        _tagRepoMock.Verify(r => r.ExistsByNameAsync("Trimmed", It.IsAny<CancellationToken>()), Times.Once);
        _tagRepoMock.Verify(r => r.AddAsync(
            It.Is<Tag>(t => t.Name == "Trimmed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddTagAsync_SetsIsLoadingDuringExecution()
    {
        var tcs = new TaskCompletionSource<bool>();
        _tagRepoMock
            .Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        vm.NewTagName = "Test";

        var addTask = vm.AddTagCommand.ExecuteAsync(null);

        Assert.True(vm.IsLoading);

        tcs.SetResult(true); // Exists, so won't add
        await addTask;

        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task AddTagAsync_ClearsNewTagName_OnSuccess()
    {
        _tagRepoMock
            .Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _tagRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tag { Id = 1, Name = "Test" });

        var vm = CreateViewModel();
        vm.NewTagName = "Test";

        await vm.AddTagCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.NewTagName);
    }

    [Fact]
    public async Task AddTagAsync_DoesNotClearNewTagName_WhenDuplicate()
    {
        _tagRepoMock
            .Setup(r => r.ExistsByNameAsync("Existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var vm = CreateViewModel();
        vm.NewTagName = "Existing";

        await vm.AddTagCommand.ExecuteAsync(null);

        // Name should NOT be cleared when tag already exists
        Assert.Equal("Existing", vm.NewTagName);
    }

    #endregion

    #region AddTagCommand CanExecute Tests

    [Fact]
    public void AddTagCommand_CannotExecute_WhenNameEmpty()
    {
        var vm = CreateViewModel();
        vm.NewTagName = "";

        Assert.False(vm.AddTagCommand.CanExecute(null));
    }

    [Fact]
    public void AddTagCommand_CannotExecute_WhenNameWhitespace()
    {
        var vm = CreateViewModel();
        vm.NewTagName = "   ";

        Assert.False(vm.AddTagCommand.CanExecute(null));
    }

    [Fact]
    public void AddTagCommand_CanExecute_WhenNameSet()
    {
        var vm = CreateViewModel();
        vm.NewTagName = "New Tag";

        Assert.True(vm.AddTagCommand.CanExecute(null));
    }

    #endregion

    #region DeleteTag Tests (Requirement 3.7)

    [Fact]
    public async Task DeleteTagAsync_RemovesSelectedTag()
    {
        var tag = new Tag { Id = 1, Name = "ToDelete" };
        _tagRepoMock
            .Setup(r => r.DeleteAsync(1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vm = CreateViewModel();
        vm.Tags.Add(tag);
        vm.SelectedTag = tag;

        await vm.DeleteTagCommand.ExecuteAsync(null);

        Assert.Empty(vm.Tags);
        Assert.Null(vm.SelectedTag);
    }

    [Fact]
    public async Task DeleteTagAsync_SetsIsLoadingDuringExecution()
    {
        var tcs = new TaskCompletionSource();
        _tagRepoMock
            .Setup(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var tag = new Tag { Id = 1, Name = "Test" };
        var vm = CreateViewModel();
        vm.Tags.Add(tag);
        vm.SelectedTag = tag;

        var deleteTask = vm.DeleteTagCommand.ExecuteAsync(null);

        Assert.True(vm.IsLoading);

        tcs.SetResult();
        await deleteTask;

        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task DeleteTagAsync_CallsRepositoryWithCorrectId()
    {
        var tag = new Tag { Id = 42, Name = "Test" };
        _tagRepoMock
            .Setup(r => r.DeleteAsync(42, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vm = CreateViewModel();
        vm.Tags.Add(tag);
        vm.SelectedTag = tag;

        await vm.DeleteTagCommand.ExecuteAsync(null);

        _tagRepoMock.Verify(r => r.DeleteAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteTagCommand CanExecute Tests

    [Fact]
    public void DeleteTagCommand_CannotExecute_WhenNoTagSelected()
    {
        var vm = CreateViewModel();

        Assert.False(vm.DeleteTagCommand.CanExecute(null));
    }

    [Fact]
    public void DeleteTagCommand_CanExecute_WhenTagSelected()
    {
        var vm = CreateViewModel();
        vm.SelectedTag = new Tag { Id = 1, Name = "Test" };

        Assert.True(vm.DeleteTagCommand.CanExecute(null));
    }

    #endregion

    #region IsLoading Guard Tests

    [Fact]
    public async Task LoadCategoriesCommand_CannotExecute_WhileLoading()
    {
        var tcs = new TaskCompletionSource<List<FolderCategory>>();
        _categoryRepoMock
            .Setup(r => r.GetTreeAsync(It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        var loadTask = vm.LoadCategoriesCommand.ExecuteAsync(null);

        Assert.False(vm.LoadCategoriesCommand.CanExecute(null));

        tcs.SetResult(new List<FolderCategory>());
        await loadTask;

        Assert.True(vm.LoadCategoriesCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddCategoryCommand_CannotExecute_WhileLoading()
    {
        var tcs = new TaskCompletionSource<List<FolderCategory>>();
        _categoryRepoMock
            .Setup(r => r.GetTreeAsync(It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        vm.NewCategoryName = "Test";

        var loadTask = vm.LoadCategoriesCommand.ExecuteAsync(null);

        Assert.False(vm.AddCategoryCommand.CanExecute(null));

        tcs.SetResult(new List<FolderCategory>());
        await loadTask;

        Assert.True(vm.AddCategoryCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddTagCommand_CannotExecute_WhileLoading()
    {
        var tcs = new TaskCompletionSource<List<Tag>>();
        _tagRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        vm.NewTagName = "Test";

        var loadTask = vm.LoadTagsCommand.ExecuteAsync(null);

        Assert.False(vm.AddTagCommand.CanExecute(null));

        tcs.SetResult(new List<Tag>());
        await loadTask;

        Assert.True(vm.AddTagCommand.CanExecute(null));
    }

    #endregion

    #region PropertyChanged Tests

    [Fact]
    public void SelectedCategory_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.SelectedCategory))
                raised = true;
        };

        vm.SelectedCategory = new FolderCategory { Id = 1, Name = "Test" };

        Assert.True(raised);
    }

    [Fact]
    public void SelectedTag_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.SelectedTag))
                raised = true;
        };

        vm.SelectedTag = new Tag { Id = 1, Name = "Test" };

        Assert.True(raised);
    }

    [Fact]
    public void NewCategoryName_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.NewCategoryName))
                raised = true;
        };

        vm.NewCategoryName = "New Name";

        Assert.True(raised);
    }

    [Fact]
    public void NewTagName_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.NewTagName))
                raised = true;
        };

        vm.NewTagName = "New Tag";

        Assert.True(raised);
    }

    [Fact]
    public void SelectedCategory_CanBeSetToNull()
    {
        var vm = CreateViewModel();
        vm.SelectedCategory = new FolderCategory { Id = 1, Name = "Test" };
        vm.SelectedCategory = null;

        Assert.Null(vm.SelectedCategory);
    }

    [Fact]
    public void SelectedTag_CanBeSetToNull()
    {
        var vm = CreateViewModel();
        vm.SelectedTag = new Tag { Id = 1, Name = "Test" };
        vm.SelectedTag = null;

        Assert.Null(vm.SelectedTag);
    }

    #endregion
}
