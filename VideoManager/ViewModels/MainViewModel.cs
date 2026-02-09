using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.ViewModels;

/// <summary>
/// Main ViewModel that coordinates top-level application logic:
/// search triggering, pagination control, refresh, and status text updates.
/// Extracted from MainWindow.xaml.cs to follow MVVM pattern.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly VideoListViewModel _videoListVm;
    private readonly SearchViewModel _searchVm;
    private readonly CategoryViewModel _categoryVm;
    private readonly IFileWatcherService _fileWatcherService;
    private readonly VideoManagerOptions _options;

    /// <summary>
    /// Status text displayed in the bottom toolbar.
    /// </summary>
    [ObservableProperty]
    private string _statusText = "就绪";

    /// <summary>
    /// Search keyword bound to the search text box.
    /// </summary>
    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    /// <summary>
    /// Page info text displayed in the video list header (e.g. "第 1/5 页 (共 100 个)").
    /// </summary>
    [ObservableProperty]
    private string _pageInfoText = "第 1 页";

    /// <summary>
    /// Cancellation token source for debounced search.
    /// </summary>
    private CancellationTokenSource? _debounceCts;

    /// <summary>
    /// Current sort field for the video list. Changing this resets pagination and reloads.
    /// </summary>
    [ObservableProperty]
    private SortField _currentSortField = SortField.ImportedAt;

    /// <summary>
    /// Current sort direction for the video list. Changing this resets pagination and reloads.
    /// </summary>
    [ObservableProperty]
    private SortDirection _currentSortDirection = SortDirection.Descending;

    /// <summary>
    /// The child VideoListViewModel for video list operations.
    /// </summary>
    public VideoListViewModel VideoListVm => _videoListVm;

    /// <summary>
    /// The child SearchViewModel for search operations.
    /// </summary>
    public SearchViewModel SearchVm => _searchVm;

    /// <summary>
    /// The child CategoryViewModel for category/tag operations.
    /// </summary>
    public CategoryViewModel CategoryVm => _categoryVm;

    /// <summary>
    /// Creates a new MainViewModel with injected child ViewModels and FileWatcher service.
    /// </summary>
    public MainViewModel(
        VideoListViewModel videoListVm,
        SearchViewModel searchVm,
        CategoryViewModel categoryVm,
        IFileWatcherService fileWatcherService,
        IOptions<VideoManagerOptions> options)
    {
        _videoListVm = videoListVm ?? throw new ArgumentNullException(nameof(videoListVm));
        _searchVm = searchVm ?? throw new ArgumentNullException(nameof(searchVm));
        _categoryVm = categoryVm ?? throw new ArgumentNullException(nameof(categoryVm));
        _fileWatcherService = fileWatcherService ?? throw new ArgumentNullException(nameof(fileWatcherService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Subscribe to VideoListViewModel property changes for page info updates
        _videoListVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(VideoListViewModel.CurrentPage)
                or nameof(VideoListViewModel.TotalPages)
                or nameof(VideoListViewModel.TotalCount))
            {
                UpdatePageInfo();
            }
        };

        // Subscribe to FileWatcher events
        _fileWatcherService.FileDeleted += OnFileDeleted;
        _fileWatcherService.FileRenamed += OnFileRenamed;
    }

    /// <summary>
    /// Loads initial data (videos, categories, tags) on application startup.
    /// Also starts file system monitoring for the video library directory.
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        try
        {
            StatusText = "正在加载数据...";

            if (_videoListVm.LoadVideosCommand.CanExecute(null))
                await _videoListVm.LoadVideosCommand.ExecuteAsync(null);

            if (_categoryVm.LoadCategoriesCommand.CanExecute(null))
                await _categoryVm.LoadCategoriesCommand.ExecuteAsync(null);

            if (_categoryVm.LoadTagsCommand.CanExecute(null))
                await _categoryVm.LoadTagsCommand.ExecuteAsync(null);

            // Start file system monitoring (Requirement 15.1)
            StartFileWatching();

            UpdatePageInfo();
            StatusText = "就绪";
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
        }
    }

    /// <summary>
    /// Executes a search with the current SearchKeyword.
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        _searchVm.Keyword = SearchKeyword;
        if (_searchVm.SearchCommand.CanExecute(null))
        {
            StatusText = "正在搜索...";
            await _searchVm.SearchCommand.ExecuteAsync(null);

            // Show search results in the video list
            _videoListVm.Videos.Clear();
            foreach (var video in _searchVm.SearchResults)
                _videoListVm.Videos.Add(video);

            _videoListVm.TotalCount = _searchVm.TotalCount;
            UpdatePageInfo();
            StatusText = $"搜索完成，找到 {_searchVm.TotalCount} 个结果";
        }
    }

    /// <summary>
    /// Clears all search filters and refreshes the video list.
    /// </summary>
    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        SearchKeyword = string.Empty;
        _searchVm.ClearFiltersCommand.Execute(null);
        await RefreshAsync();
    }

    /// <summary>
    /// Refreshes the video list by reloading from the repository.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        StatusText = "正在刷新...";
        if (_videoListVm.LoadVideosCommand.CanExecute(null))
            await _videoListVm.LoadVideosCommand.ExecuteAsync(null);
        UpdatePageInfo();
        StatusText = "就绪";
    }

    /// <summary>
    /// Navigates to the previous page.
    /// </summary>
    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (_videoListVm.PreviousPageCommand.CanExecute(null))
        {
            await _videoListVm.PreviousPageCommand.ExecuteAsync(null);
            UpdatePageInfo();
        }
    }

    /// <summary>
    /// Navigates to the next page.
    /// </summary>
    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (_videoListVm.NextPageCommand.CanExecute(null))
        {
            await _videoListVm.NextPageCommand.ExecuteAsync(null);
            UpdatePageInfo();
        }
    }

    /// <summary>
    /// Triggered when SearchKeyword changes. Starts debounced search.
    /// </summary>
    partial void OnSearchKeywordChanged(string value)
    {
        _ = DebouncedSearchAsync(value);
    }

    /// <summary>
    /// Triggered when CurrentSortField changes. Syncs to VideoListVm, resets pagination, and reloads.
    /// </summary>
    partial void OnCurrentSortFieldChanged(SortField value)
    {
        _videoListVm.SortField = value;
        _ = ResetAndReloadAsync();
    }

    /// <summary>
    /// Triggered when CurrentSortDirection changes. Syncs to VideoListVm, resets pagination, and reloads.
    /// </summary>
    partial void OnCurrentSortDirectionChanged(SortDirection value)
    {
        _videoListVm.SortDirection = value;
        _ = ResetAndReloadAsync();
    }

    /// <summary>
    /// Toggles the sort direction between Ascending and Descending.
    /// </summary>
    [RelayCommand]
    private void ToggleSortDirection()
    {
        CurrentSortDirection = CurrentSortDirection == SortDirection.Ascending
            ? SortDirection.Descending
            : SortDirection.Ascending;
    }

    /// <summary>
    /// Resets pagination to page 1 and reloads the video list.
    /// Used when sort criteria change.
    /// </summary>
    private async Task ResetAndReloadAsync()
    {
        _videoListVm.CurrentPage = 1;
        await RefreshAsync();
    }

    /// <summary>
    /// Implements 300ms debounce for search. Cancels previous pending search
    /// when new input arrives.
    /// </summary>
    private async Task DebouncedSearchAsync(string keyword)
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        try
        {
            await Task.Delay(300, token);
            if (!token.IsCancellationRequested)
            {
                await ExecuteSearchAsync(keyword, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when debounce is reset by new input
        }
    }

    /// <summary>
    /// Executes the actual search after debounce completes.
    /// If keyword is empty, restores the full video list.
    /// </summary>
    private async Task ExecuteSearchAsync(string keyword, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            // Empty keyword: restore full video list
            await RefreshAsync();
            return;
        }

        _searchVm.Keyword = keyword;
        if (_searchVm.SearchCommand.CanExecute(null))
        {
            StatusText = "正在搜索...";
            await _searchVm.SearchCommand.ExecuteAsync(null);

            if (token.IsCancellationRequested) return;

            _videoListVm.Videos.Clear();
            foreach (var video in _searchVm.SearchResults)
                _videoListVm.Videos.Add(video);

            _videoListVm.TotalCount = _searchVm.TotalCount;
            UpdatePageInfo();
            StatusText = $"搜索完成，找到 {_searchVm.TotalCount} 个结果";
        }
    }

    /// <summary>
    /// Updates the page info text based on current pagination state.
    /// </summary>
    private void UpdatePageInfo()
    {
        PageInfoText = $"第 {_videoListVm.CurrentPage}/{_videoListVm.TotalPages} 页 (共 {_videoListVm.TotalCount} 个)";
    }

    /// <summary>
    /// Starts file system monitoring for the video library directory.
    /// Failures are logged and the application continues normally (Requirement 15.4).
    /// </summary>
    private void StartFileWatching()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_options.VideoLibraryPath))
            {
                _fileWatcherService.StartWatching(_options.VideoLibraryPath);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[MainViewModel] Failed to start file watching: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles file deletion events from the FileWatcher.
    /// Marks the corresponding VideoEntry as IsFileMissing = true (Requirement 15.2).
    /// </summary>
    private void OnFileDeleted(object? sender, FileDeletedEventArgs e)
    {
        var video = FindVideoByFilePath(e.FilePath);
        if (video is not null)
        {
            video.IsFileMissing = true;
        }
    }

    /// <summary>
    /// Handles file rename events from the FileWatcher.
    /// Updates the corresponding VideoEntry's FilePath to the new path (Requirement 15.3).
    /// </summary>
    private void OnFileRenamed(object? sender, FileRenamedEventArgs e)
    {
        var video = FindVideoByFilePath(e.OldPath);
        if (video is not null)
        {
            video.FilePath = e.NewPath;
        }
    }

    /// <summary>
    /// Finds a VideoEntry in the current video list by its file path (case-insensitive).
    /// </summary>
    private VideoEntry? FindVideoByFilePath(string filePath)
    {
        return _videoListVm.Videos.FirstOrDefault(v =>
            string.Equals(v.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
    }
}
