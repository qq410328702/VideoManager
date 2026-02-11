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
    private readonly IThumbnailPriorityLoader? _thumbnailPriorityLoader;

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
    /// <param name="thumbnailPriorityLoader">
    /// Optional priority loader that schedules thumbnail loading with visible-item priority.
    /// </param>
    public VideoListViewModel(IVideoRepository videoRepository, IThumbnailPriorityLoader? thumbnailPriorityLoader = null)
    {
        _videoRepository = videoRepository ?? throw new ArgumentNullException(nameof(videoRepository));
        _thumbnailPriorityLoader = thumbnailPriorityLoader;

        if (_thumbnailPriorityLoader != null)
        {
            _thumbnailPriorityLoader.ThumbnailLoaded += OnThumbnailLoaded;
        }

        SelectedVideos.CollectionChanged += (_, _) => UpdateSelectionState();
    }

    /// <summary>
    /// Handles the ThumbnailLoaded event from the priority loader.
    /// Updates the corresponding video's ThumbnailPath when a thumbnail is loaded.
    /// </summary>
    private void OnThumbnailLoaded(int videoId, string? resolvedPath)
    {
        if (resolvedPath == null) return;

        var video = Videos.FirstOrDefault(v => v.Id == videoId);
        if (video != null)
        {
            video.ThumbnailPath = resolvedPath;
        }
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

            // Schedule thumbnail loading via priority loader (non-blocking)
            if (_thumbnailPriorityLoader != null)
            {
                foreach (var video in result.Items)
                {
                    if (!string.IsNullOrEmpty(video.ThumbnailPath))
                    {
                        _thumbnailPriorityLoader.Enqueue(video.Id, video.ThumbnailPath, isVisible: true);
                    }
                }
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
    /// Calculates total pages from total count and page size.
    /// </summary>
    internal static int CalculateTotalPages(int totalCount, int pageSize)
    {
        if (pageSize <= 0) return 0;
        return (int)Math.Ceiling((double)totalCount / pageSize);
    }
}
