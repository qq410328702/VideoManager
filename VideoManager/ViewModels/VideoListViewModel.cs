using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;

namespace VideoManager.ViewModels;

/// <summary>
/// ViewModel for the video list view. Manages paginated video loading,
/// selection tracking, multi-selection for batch operations, and async thumbnail loading.
/// </summary>
public partial class VideoListViewModel : ViewModelBase
{
    private readonly IVideoRepository _videoRepository;
    private readonly Func<string, Task<string?>>? _thumbnailLoader;

    /// <summary>
    /// The collection of videos currently displayed.
    /// </summary>
    public ObservableCollection<VideoEntry> Videos { get; } = new();

    /// <summary>
    /// The collection of currently selected videos for batch operations.
    /// </summary>
    public ObservableCollection<VideoEntry> SelectedVideos { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private int _totalPages;

    [ObservableProperty]
    private int _pageSize = 50;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private VideoEntry? _selectedVideo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadVideosCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private bool _isLoading;

    /// <summary>
    /// Whether multiple videos are currently selected (for batch operation toolbar visibility).
    /// </summary>
    [ObservableProperty]
    private bool _hasMultipleSelection;

    /// <summary>
    /// Text describing the current selection count.
    /// </summary>
    [ObservableProperty]
    private string _selectionInfoText = string.Empty;

    /// <summary>
    /// Whether a batch operation is currently in progress.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelBatchCommand))]
    private bool _isBatchOperating;

    /// <summary>
    /// Progress text for batch operations.
    /// </summary>
    [ObservableProperty]
    private string _batchProgressText = string.Empty;

    /// <summary>
    /// Progress percentage for batch operations (0-100).
    /// </summary>
    [ObservableProperty]
    private double _batchProgressPercentage;

    /// <summary>
    /// Estimated time remaining text for batch operations.
    /// </summary>
    [ObservableProperty]
    private string _batchEstimatedTimeRemaining = string.Empty;

    /// <summary>
    /// CancellationTokenSource for the current batch operation.
    /// </summary>
    private CancellationTokenSource? _batchCancellationTokenSource;

    /// <summary>
    /// Gets a CancellationToken for the current batch operation.
    /// Creates a new CancellationTokenSource if one doesn't exist.
    /// </summary>
    public CancellationToken BeginBatchOperation()
    {
        _batchCancellationTokenSource?.Dispose();
        _batchCancellationTokenSource = new CancellationTokenSource();
        IsBatchOperating = true;
        BatchProgressText = string.Empty;
        BatchProgressPercentage = 0;
        BatchEstimatedTimeRemaining = string.Empty;
        return _batchCancellationTokenSource.Token;
    }

    /// <summary>
    /// Ends the current batch operation and cleans up resources.
    /// </summary>
    public void EndBatchOperation()
    {
        IsBatchOperating = false;
        BatchProgressText = string.Empty;
        BatchProgressPercentage = 0;
        BatchEstimatedTimeRemaining = string.Empty;
        _batchCancellationTokenSource?.Dispose();
        _batchCancellationTokenSource = null;
    }

    /// <summary>
    /// Cancels the current batch operation.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelBatch))]
    private void CancelBatch()
    {
        _batchCancellationTokenSource?.Cancel();
    }

    private bool CanCancelBatch() => IsBatchOperating;

    /// <summary>
    /// Formats a TimeSpan into a human-readable remaining time string.
    /// </summary>
    internal static string FormatTimeRemaining(TimeSpan? timeRemaining)
    {
        if (timeRemaining is null)
            return string.Empty;

        var ts = timeRemaining.Value;
        if (ts.TotalHours >= 1)
            return $"预计剩余 {(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        if (ts.TotalMinutes >= 1)
            return $"预计剩余 {(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        return $"预计剩余 {(int)ts.TotalSeconds} 秒";
    }

    /// <summary>
    /// Current sort field used when loading videos.
    /// </summary>
    [ObservableProperty]
    private SortField _sortField = SortField.ImportedAt;

    /// <summary>
    /// Current sort direction used when loading videos.
    /// </summary>
    [ObservableProperty]
    private SortDirection _sortDirection = SortDirection.Descending;

    /// <summary>
    /// Creates a new VideoListViewModel.
    /// </summary>
    /// <param name="videoRepository">The video repository for data access.</param>
    /// <param name="thumbnailLoader">
    /// Optional async function that loads a thumbnail given a thumbnail path.
    /// Returns the resolved path (or null on failure). Used for lazy thumbnail loading.
    /// </param>
    public VideoListViewModel(IVideoRepository videoRepository, Func<string, Task<string?>>? thumbnailLoader = null)
    {
        _videoRepository = videoRepository ?? throw new ArgumentNullException(nameof(videoRepository));
        _thumbnailLoader = thumbnailLoader;

        SelectedVideos.CollectionChanged += (_, _) => UpdateSelectionState();
    }

    /// <summary>
    /// Updates the selection state properties based on the current SelectedVideos collection.
    /// </summary>
    private void UpdateSelectionState()
    {
        HasMultipleSelection = SelectedVideos.Count > 1;
        SelectionInfoText = SelectedVideos.Count > 1
            ? $"已选择 {SelectedVideos.Count} 个视频"
            : string.Empty;
    }

    /// <summary>
    /// Gets the IDs of all currently selected videos.
    /// </summary>
    public List<int> GetSelectedVideoIds()
    {
        return SelectedVideos.Select(v => v.Id).ToList();
    }

    /// <summary>
    /// Clears the multi-selection.
    /// </summary>
    [RelayCommand]
    private void ClearSelection()
    {
        SelectedVideos.Clear();
    }

    /// <summary>
    /// Loads the current page of videos from the repository.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadVideos))]
    private async Task LoadVideosAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            var result = await _videoRepository.GetPagedAsync(CurrentPage, PageSize, ct, SortField, SortDirection);

            Videos.Clear();
            SelectedVideos.Clear();
            foreach (var video in result.Items)
            {
                Videos.Add(video);
            }

            TotalCount = result.TotalCount;
            TotalPages = CalculateTotalPages(result.TotalCount, PageSize);

            // Ensure CurrentPage is valid after loading
            if (CurrentPage > TotalPages && TotalPages > 0)
            {
                CurrentPage = TotalPages;
            }

            // Fire async thumbnail loading (fire-and-forget, non-blocking)
            // Use CancellationToken.None since thumbnail loading should continue
            // even after the main load command completes.
            if (_thumbnailLoader != null)
            {
                _ = LoadThumbnailsAsync(result.Items, CancellationToken.None);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanLoadVideos() => !IsLoading;

    /// <summary>
    /// Navigates to the next page and loads videos.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoNextPage))]
    private async Task NextPageAsync(CancellationToken ct = default)
    {
        CurrentPage++;
        await LoadVideosAsync(ct);
    }

    private bool CanGoNextPage() => !IsLoading && CurrentPage < TotalPages;

    /// <summary>
    /// Navigates to the previous page and loads videos.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoPreviousPage))]
    private async Task PreviousPageAsync(CancellationToken ct = default)
    {
        CurrentPage--;
        await LoadVideosAsync(ct);
    }

    private bool CanGoPreviousPage() => !IsLoading && CurrentPage > 1;

    /// <summary>
    /// Navigates to a specific page and loads videos.
    /// </summary>
    public async Task GoToPageAsync(int page, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (TotalPages > 0 && page > TotalPages) page = TotalPages;

        CurrentPage = page;
        await LoadVideosAsync(ct);
    }

    /// <summary>
    /// Asynchronously loads thumbnails for the given video entries.
    /// This runs in the background and updates ThumbnailPath as thumbnails are resolved.
    /// </summary>
    private async Task LoadThumbnailsAsync(IEnumerable<VideoEntry> videos, CancellationToken ct)
    {
        if (_thumbnailLoader == null) return;

        foreach (var video in videos)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrEmpty(video.ThumbnailPath)) continue;

            try
            {
                var resolvedPath = await _thumbnailLoader(video.ThumbnailPath);
                if (resolvedPath != null)
                {
                    video.ThumbnailPath = resolvedPath;
                }
            }
            catch
            {
                // Thumbnail loading failure is non-critical; skip silently
            }
        }
    }

    /// <summary>
    /// Calculates total pages from total count and page size.
    /// </summary>
    internal static int CalculateTotalPages(int totalCount, int pageSize)
    {
        if (pageSize <= 0) return 0;
        return (int)Math.Ceiling((double)totalCount / pageSize);
    }
}
