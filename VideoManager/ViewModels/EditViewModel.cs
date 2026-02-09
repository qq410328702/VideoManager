using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;

namespace VideoManager.ViewModels;

/// <summary>
/// ViewModel for the video metadata edit dialog. Manages editing of title,
/// description, tag associations, and category associations.
/// </summary>
public partial class EditViewModel : ViewModelBase
{
    private readonly IEditService _editService;
    private readonly ITagRepository _tagRepository;
    private readonly ICategoryRepository _categoryRepository;

    /// <summary>
    /// Material Design color palette for tag color selection.
    /// </summary>
    public static readonly string[] MaterialDesignColors =
    [
        "#F44336", // Red
        "#E91E63", // Pink
        "#9C27B0", // Purple
        "#673AB7", // Deep Purple
        "#3F51B5", // Indigo
        "#2196F3", // Blue
        "#03A9F4", // Light Blue
        "#00BCD4", // Cyan
        "#009688", // Teal
        "#4CAF50", // Green
        "#8BC34A", // Light Green
        "#CDDC39", // Lime
        "#FFEB3B", // Yellow
        "#FFC107", // Amber
        "#FF9800", // Orange
        "#FF5722", // Deep Orange
        "#795548", // Brown
        "#607D8B", // Blue Grey
    ];

    /// <summary>
    /// The current tags associated with the video being edited.
    /// </summary>
    public ObservableCollection<Tag> VideoTags { get; } = new();

    /// <summary>
    /// All available tags that can be added to the video.
    /// </summary>
    public ObservableCollection<Tag> AvailableTags { get; } = new();

    /// <summary>
    /// The current categories associated with the video being edited.
    /// </summary>
    public ObservableCollection<FolderCategory> VideoCategories { get; } = new();

    /// <summary>
    /// All available categories that can be added to the video.
    /// </summary>
    public ObservableCollection<FolderCategory> AvailableCategories { get; } = new();

    /// <summary>
    /// The color palette exposed for the UI.
    /// </summary>
    public ObservableCollection<string> ColorPalette { get; } = new(MaterialDesignColors);

    /// <summary>
    /// The video entry being edited.
    /// </summary>
    [ObservableProperty]
    private VideoEntry? _video;

    /// <summary>
    /// The editable title of the video.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _title = string.Empty;

    /// <summary>
    /// The editable description of the video.
    /// </summary>
    [ObservableProperty]
    private string? _description;

    /// <summary>
    /// The tag selected from the available tags list.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    private Tag? _selectedAvailableTag;

    /// <summary>
    /// The tag selected from the video's current tags list.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveTagCommand))]
    private Tag? _selectedVideoTag;

    /// <summary>
    /// The category selected from the available categories list.
    /// </summary>
    [ObservableProperty]
    private FolderCategory? _selectedAvailableCategory;

    /// <summary>
    /// The category selected from the video's current categories list.
    /// </summary>
    [ObservableProperty]
    private FolderCategory? _selectedVideoCategory;

    /// <summary>
    /// Whether a save operation is currently in progress.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveTagCommand))]
    private bool _isSaving;

    /// <summary>
    /// Whether there is a validation or operation error.
    /// </summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// The error message to display to the user.
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Whether the color picker palette is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isColorPickerVisible;

    /// <summary>
    /// Title text for the color picker panel showing which tag is being colored.
    /// </summary>
    [ObservableProperty]
    private string _colorPickerTitle = string.Empty;

    /// <summary>
    /// The tag currently being color-edited.
    /// </summary>
    private Tag? _colorPickerTag;

    /// <summary>
    /// Raised when save succeeds and the dialog should close.
    /// </summary>
    public event Action? SaveCompleted;

    /// <summary>
    /// Creates a new EditViewModel.
    /// </summary>
    /// <param name="editService">The edit service for updating video metadata.</param>
    /// <param name="tagRepository">The tag repository for loading available tags.</param>
    public EditViewModel(IEditService editService, ITagRepository tagRepository, ICategoryRepository categoryRepository)
    {
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
        _tagRepository = tagRepository ?? throw new ArgumentNullException(nameof(tagRepository));
        _categoryRepository = categoryRepository ?? throw new ArgumentNullException(nameof(categoryRepository));
    }

    /// <summary>
    /// Loads a video entry into the edit form, populating all fields
    /// and loading available tags.
    /// </summary>
    /// <param name="video">The video entry to edit.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadVideoAsync(VideoEntry video, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(video);

        Video = video;
        Title = video.Title;
        Description = video.Description;

        ClearError();

        VideoTags.Clear();
        foreach (var tag in video.Tags)
        {
            VideoTags.Add(tag);
        }

        // Load all available tags
        var allTags = await _tagRepository.GetAllAsync(ct);

        AvailableTags.Clear();
        foreach (var tag in allTags)
        {
            if (!VideoTags.Any(vt => vt.Id == tag.Id))
            {
                AvailableTags.Add(tag);
            }
        }

        // Load video categories
        VideoCategories.Clear();
        foreach (var category in video.Categories)
        {
            VideoCategories.Add(category);
        }

        // Load all available categories (flattened from tree)
        var allCategories = await _categoryRepository.GetTreeAsync(ct);
        var flatCategories = FlattenCategories(allCategories);

        AvailableCategories.Clear();
        foreach (var category in flatCategories)
        {
            if (!VideoCategories.Any(vc => vc.Id == category.Id))
            {
                AvailableCategories.Add(category);
            }
        }
    }

    /// <summary>
    /// Flattens a tree of categories into a flat list.
    /// </summary>
    private static List<FolderCategory> FlattenCategories(IEnumerable<FolderCategory> categories)
    {
        var result = new List<FolderCategory>();
        foreach (var category in categories)
        {
            result.Add(category);
            if (category.Children.Count > 0)
            {
                result.AddRange(FlattenCategories(category.Children));
            }
        }
        return result;
    }

    /// <summary>
    /// Saves the edited title and description to the database.
    /// Validates that the title is not empty or whitespace.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (Video == null) return;

        if (string.IsNullOrWhiteSpace(Title))
        {
            SetError("标题不能为空");
            return;
        }

        IsSaving = true;
        ClearError();

        try
        {
            var updatedVideo = await _editService.UpdateVideoInfoAsync(
                Video.Id, Title.Trim(), Description?.Trim(), ct);

            Video = updatedVideo;
            Title = updatedVideo.Title;
            Description = updatedVideo.Description;

            SaveCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            SetError($"保存失败: {ex.Message}");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanSave() => !IsSaving && !string.IsNullOrWhiteSpace(Title);

    /// <summary>
    /// Adds the selected available tag to the video.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddTag))]
    private async Task AddTagAsync(CancellationToken ct = default)
    {
        if (Video == null || SelectedAvailableTag == null) return;

        IsSaving = true;
        ClearError();

        try
        {
            var tagToAdd = SelectedAvailableTag;

            await _editService.AddTagToVideoAsync(Video.Id, tagToAdd.Id, ct);

            VideoTags.Add(tagToAdd);
            AvailableTags.Remove(tagToAdd);
            SelectedAvailableTag = null;
        }
        catch (KeyNotFoundException ex)
        {
            SetError(ex.Message);
        }
        catch (Exception ex)
        {
            SetError($"添加标签失败: {ex.Message}");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanAddTag() => !IsSaving && SelectedAvailableTag != null;

    /// <summary>
    /// Removes the selected tag from the video.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveTag))]
    private async Task RemoveTagAsync(CancellationToken ct = default)
    {
        if (Video == null || SelectedVideoTag == null) return;

        IsSaving = true;
        ClearError();

        try
        {
            var tagToRemove = SelectedVideoTag;

            await _editService.RemoveTagFromVideoAsync(Video.Id, tagToRemove.Id, ct);

            VideoTags.Remove(tagToRemove);
            AvailableTags.Add(tagToRemove);
            SelectedVideoTag = null;
        }
        catch (KeyNotFoundException ex)
        {
            SetError(ex.Message);
        }
        catch (Exception ex)
        {
            SetError($"移除标签失败: {ex.Message}");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanRemoveTag() => !IsSaving && SelectedVideoTag != null;

    /// <summary>
    /// Adds the selected available category to the video.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddCategory))]
    private async Task AddCategoryAsync(FolderCategory? category, CancellationToken ct = default)
    {
        if (Video == null || category == null) return;

        IsSaving = true;
        ClearError();

        try
        {
            await _editService.AddCategoryToVideoAsync(Video.Id, category.Id, ct);

            VideoCategories.Add(category);
            AvailableCategories.Remove(category);
        }
        catch (KeyNotFoundException ex)
        {
            SetError(ex.Message);
        }
        catch (Exception ex)
        {
            SetError($"添加分类失败: {ex.Message}");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanAddCategory(FolderCategory? category) => !IsSaving && category != null;

    /// <summary>
    /// Removes the selected category from the video.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveCategory))]
    private async Task RemoveCategoryAsync(FolderCategory? category, CancellationToken ct = default)
    {
        if (Video == null || category == null) return;

        IsSaving = true;
        ClearError();

        try
        {
            await _editService.RemoveCategoryFromVideoAsync(Video.Id, category.Id, ct);

            VideoCategories.Remove(category);
            AvailableCategories.Add(category);
        }
        catch (KeyNotFoundException ex)
        {
            SetError(ex.Message);
        }
        catch (Exception ex)
        {
            SetError($"移除分类失败: {ex.Message}");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanRemoveCategory(FolderCategory? category) => !IsSaving && category != null;

    /// <summary>
    /// Opens the color picker for the specified tag.
    /// </summary>
    [RelayCommand]
    private void PickColor(Tag? tag)
    {
        if (tag == null) return;

        _colorPickerTag = tag;
        ColorPickerTitle = $"为「{tag.Name}」选择颜色";
        IsColorPickerVisible = true;
    }

    /// <summary>
    /// Applies the selected color to the tag being edited.
    /// When called with null parameter (from the "默认颜色" button), resets to default.
    /// </summary>
    [RelayCommand]
    private async Task SelectColorAsync(string? color)
    {
        if (_colorPickerTag == null) return;

        ClearError();

        try
        {
            await _editService.UpdateTagColorAsync(_colorPickerTag.Id, color, CancellationToken.None);

            // Update the tag's color in the local collections
            _colorPickerTag.Color = color;

            // Force UI refresh by replacing the tag in the collection
            var index = -1;
            for (int i = 0; i < VideoTags.Count; i++)
            {
                if (VideoTags[i].Id == _colorPickerTag.Id)
                {
                    index = i;
                    break;
                }
            }
            if (index >= 0)
            {
                VideoTags.RemoveAt(index);
                VideoTags.Insert(index, _colorPickerTag);
            }

            // Also check available tags
            for (int i = 0; i < AvailableTags.Count; i++)
            {
                if (AvailableTags[i].Id == _colorPickerTag.Id)
                {
                    AvailableTags.RemoveAt(i);
                    AvailableTags.Insert(i, _colorPickerTag);
                    break;
                }
            }
        }
        catch (KeyNotFoundException ex)
        {
            SetError(ex.Message);
        }
        catch (Exception ex)
        {
            SetError($"更新颜色失败: {ex.Message}");
        }
        finally
        {
            IsColorPickerVisible = false;
            _colorPickerTag = null;
        }
    }

    /// <summary>
    /// Cancels the color picker without making changes.
    /// </summary>
    [RelayCommand]
    private void CancelColorPicker()
    {
        IsColorPickerVisible = false;
        _colorPickerTag = null;
    }

    /// <summary>
    /// Sets an error message and marks HasError as true.
    /// </summary>
    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    /// <summary>
    /// Clears any existing error state.
    /// </summary>
    private void ClearError()
    {
        ErrorMessage = string.Empty;
        HasError = false;
    }
}
