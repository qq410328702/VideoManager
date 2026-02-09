using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoManager.Models;
using VideoManager.Repositories;

namespace VideoManager.ViewModels;

/// <summary>
/// ViewModel for the category management panel. Manages the folder category
/// tree structure and tag list, with add/delete operations for both.
/// </summary>
public partial class CategoryViewModel : ViewModelBase
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ITagRepository _tagRepository;

    /// <summary>
    /// The collection of root-level folder categories (tree structure).
    /// </summary>
    public ObservableCollection<FolderCategory> Categories { get; } = new();

    /// <summary>
    /// The collection of all tags.
    /// </summary>
    public ObservableCollection<Tag> Tags { get; } = new();

    /// <summary>
    /// The currently selected folder category.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCategoryCommand))]
    private FolderCategory? _selectedCategory;

    /// <summary>
    /// The currently selected tag.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteTagCommand))]
    private Tag? _selectedTag;

    /// <summary>
    /// The name for a new category to be created.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCategoryCommand))]
    private string _newCategoryName = string.Empty;

    /// <summary>
    /// The name for a new tag to be created.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    private string _newTagName = string.Empty;

    /// <summary>
    /// Whether a loading operation is currently in progress.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCategoriesCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadTagsCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCategoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCategoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteTagCommand))]
    private bool _isLoading;

    /// <summary>
    /// Creates a new CategoryViewModel.
    /// </summary>
    /// <param name="categoryRepository">The category repository for data access.</param>
    /// <param name="tagRepository">The tag repository for data access.</param>
    public CategoryViewModel(ICategoryRepository categoryRepository, ITagRepository tagRepository)
    {
        _categoryRepository = categoryRepository ?? throw new ArgumentNullException(nameof(categoryRepository));
        _tagRepository = tagRepository ?? throw new ArgumentNullException(nameof(tagRepository));
    }

    /// <summary>
    /// Loads root categories (tree structure) from the repository.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadCategoriesAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            var categories = await _categoryRepository.GetTreeAsync(ct);

            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads all tags from the repository.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadTagsAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            var tags = await _tagRepository.GetAllAsync(ct);

            Tags.Clear();
            foreach (var tag in tags)
            {
                Tags.Add(tag);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanLoad() => !IsLoading;

    /// <summary>
    /// Adds a new category. If a category is selected, the new category is added
    /// as a child of the selected category; otherwise it is added as a root category.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddCategory))]
    private async Task AddCategoryAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            var newCategory = new FolderCategory
            {
                Name = NewCategoryName.Trim(),
                ParentId = SelectedCategory?.Id
            };

            var added = await _categoryRepository.AddAsync(newCategory, ct);

            if (SelectedCategory != null)
            {
                SelectedCategory.Children.Add(added);
            }
            else
            {
                Categories.Add(added);
            }

            NewCategoryName = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanAddCategory() => !IsLoading && !string.IsNullOrWhiteSpace(NewCategoryName);

    /// <summary>
    /// Deletes the currently selected category.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteCategory))]
    private async Task DeleteCategoryAsync(CancellationToken ct = default)
    {
        if (SelectedCategory == null) return;

        IsLoading = true;
        try
        {
            var categoryToDelete = SelectedCategory;
            await _categoryRepository.DeleteAsync(categoryToDelete.Id, ct);

            // Remove from parent's children or from root collection
            if (categoryToDelete.ParentId != null)
            {
                var parent = FindCategoryById(Categories, categoryToDelete.ParentId.Value);
                parent?.Children.Remove(categoryToDelete);
            }
            else
            {
                Categories.Remove(categoryToDelete);
            }

            SelectedCategory = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanDeleteCategory() => !IsLoading && SelectedCategory != null;

    /// <summary>
    /// Adds a new tag with uniqueness check.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddTag))]
    private async Task AddTagAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            var trimmedName = NewTagName.Trim();

            // Check uniqueness before adding
            var exists = await _tagRepository.ExistsByNameAsync(trimmedName, ct);
            if (exists)
            {
                return;
            }

            var newTag = new Tag { Name = trimmedName };
            var added = await _tagRepository.AddAsync(newTag, ct);

            Tags.Add(added);
            NewTagName = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanAddTag() => !IsLoading && !string.IsNullOrWhiteSpace(NewTagName);

    /// <summary>
    /// Deletes the currently selected tag.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteTag))]
    private async Task DeleteTagAsync(CancellationToken ct = default)
    {
        if (SelectedTag == null) return;

        IsLoading = true;
        try
        {
            var tagToDelete = SelectedTag;
            await _tagRepository.DeleteAsync(tagToDelete.Id, ct);

            Tags.Remove(tagToDelete);
            SelectedTag = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanDeleteTag() => !IsLoading && SelectedTag != null;

    /// <summary>
    /// Recursively searches for a category by ID in the tree structure.
    /// </summary>
    private static FolderCategory? FindCategoryById(IEnumerable<FolderCategory> categories, int id)
    {
        foreach (var category in categories)
        {
            if (category.Id == id)
                return category;

            var found = FindCategoryById(category.Children, id);
            if (found != null)
                return found;
        }

        return null;
    }
}
