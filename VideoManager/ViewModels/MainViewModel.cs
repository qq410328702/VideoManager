using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.ViewModels;

/// <summary>
/// Main ViewModel that coordinates top-level application logic:
/// search triggering, refresh, status text updates, navigation, and dialog management.
/// Pagination, sorting, and batch operations are delegated to sub-ViewModels
/// (PaginationViewModel, SortViewModel, BatchOperationViewModel).
/// MainViewModel receives messages from sub-ViewModels via WeakReferenceMessenger
/// and coordinates responses (e.g. reloading the video list when sort changes).
/// </summary>
public partial class MainViewModel : ViewModelBase,
    IRecipient<SortChangedMessage>,
    IRecipient<RefreshRequestedMessage>,
    IRecipient<BatchOperationCompletedMessage>,
    IRecipient<PageChangedMessage>
{
    private readonly VideoListViewModel _videoListVm;
    private readonly SearchViewModel _searchVm;
    private readonly CategoryViewModel _categoryVm;
    private readonly PaginationViewModel _paginationVm;
    private readonly SortViewModel _sortVm;
    private readonly BatchOperationViewModel _batchOperationVm;
    private readonly IFileWatcherService _fileWatcherService;
    private readonly VideoManagerOptions _options;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;

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
    /// Cancellation token source for debounced search.
    /// </summary>
    private CancellationTokenSource? _debounceCts;

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
    /// The child PaginationViewModel for pagination controls.
    /// </summary>
    public PaginationViewModel PaginationVm => _paginationVm;

    /// <summary>
    /// The child SortViewModel for sorting controls.
    /// </summary>
    public SortViewModel SortVm => _sortVm;

    /// <summary>
    /// The child BatchOperationViewModel for batch operations.
    /// </summary>
    public BatchOperationViewModel BatchOperationVm => _batchOperationVm;

    /// <summary>
    /// Creates a new MainViewModel with injected child ViewModels, services, and FileWatcher service.
    /// </summary>
    public MainViewModel(
        VideoListViewModel videoListVm,
        SearchViewModel searchVm,
        CategoryViewModel categoryVm,
        PaginationViewModel paginationVm,
        SortViewModel sortVm,
        BatchOperationViewModel batchOperationVm,
        IFileWatcherService fileWatcherService,
        IOptions<VideoManagerOptions> options,
        INavigationService navigationService,
        IDialogService dialogService,
        IServiceProvider serviceProvider)
    {
        _videoListVm = videoListVm ?? throw new ArgumentNullException(nameof(videoListVm));
        _searchVm = searchVm ?? throw new ArgumentNullException(nameof(searchVm));
        _categoryVm = categoryVm ?? throw new ArgumentNullException(nameof(categoryVm));
        _paginationVm = paginationVm ?? throw new ArgumentNullException(nameof(paginationVm));
        _sortVm = sortVm ?? throw new ArgumentNullException(nameof(sortVm));
        _batchOperationVm = batchOperationVm ?? throw new ArgumentNullException(nameof(batchOperationVm));
        _fileWatcherService = fileWatcherService ?? throw new ArgumentNullException(nameof(fileWatcherService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        // Subscribe to FileWatcher events
        _fileWatcherService.FileDeleted += OnFileDeleted;
        _fileWatcherService.FileRenamed += OnFileRenamed;

        // Register as Messenger recipient for sub-ViewModel messages
        WeakReferenceMessenger.Default.Register<SortChangedMessage>(this);
        WeakReferenceMessenger.Default.Register<RefreshRequestedMessage>(this);
        WeakReferenceMessenger.Default.Register<BatchOperationCompletedMessage>(this);
        WeakReferenceMessenger.Default.Register<PageChangedMessage>(this);
    }

    // ==================== Messenger Handlers ====================

    /// <summary>
    /// Handles sort changes from SortViewModel.
    /// Syncs the new sort criteria to VideoListViewModel and reloads.
    /// </summary>
    public void Receive(SortChangedMessage message)
    {
        _videoListVm.SortField = message.Field;
        _videoListVm.SortDirection = message.Direction;
        _ = ResetAndReloadAsync();
    }

    /// <summary>
    /// Handles refresh requests from sub-ViewModels (e.g. after batch operations).
    /// </summary>
    public void Receive(RefreshRequestedMessage message)
    {
        _ = RefreshAsync();
    }

    /// <summary>
    /// Handles batch operation completion messages.
    /// Updates status text with the operation result.
    /// </summary>
    public void Receive(BatchOperationCompletedMessage message)
    {
        StatusText = $"{message.OperationType} 完成: 成功 {message.SuccessCount}, 失败 {message.FailCount}";
    }

    /// <summary>
    /// Handles page change messages from PaginationViewModel.
    /// Updates status text to reflect the current page.
    /// </summary>
    public void Receive(PageChangedMessage message)
    {
        StatusText = $"第 {message.NewPage} 页";
    }

    // ==================== Commands ====================

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

            // Start compensation scan (Requirement 5.1)
            _fileWatcherService.StartCompensationScan(_options.CompensationScanIntervalHours);

            // Sync pagination state after initial load
            _paginationVm.SyncFromVideoListVm();

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
            _paginationVm.SyncFromVideoListVm();
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
        _paginationVm.SyncFromVideoListVm();
        StatusText = "就绪";
    }

    /// <summary>
    /// Triggered when SearchKeyword changes. Starts debounced search.
    /// </summary>
    partial void OnSearchKeywordChanged(string value)
    {
        _ = DebouncedSearchAsync(value);
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
            _paginationVm.SyncFromVideoListVm();
            StatusText = $"搜索完成，找到 {_searchVm.TotalCount} 个结果";
        }
    }

    // ==================== FileWatcher ====================

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

    // ==================== Navigation & Dialog Commands ====================

    /// <summary>
    /// Opens the video player for the currently selected video.
    /// </summary>
    [RelayCommand]
    private async Task OpenVideoPlayerAsync()
    {
        if (_videoListVm.SelectedVideo is VideoEntry video)
        {
            await _navigationService.OpenVideoPlayerAsync(video);
        }
    }

    /// <summary>
    /// Opens the import dialog and refreshes the video list if videos were imported.
    /// </summary>
    [RelayCommand]
    private async Task ImportVideosAsync()
    {
        var result = await _navigationService.OpenImportDialogAsync();

        if (result != null && result.SuccessCount > 0)
        {
            if (RefreshCommand.CanExecute(null))
                await RefreshCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Opens the diagnostics window showing performance metrics, memory usage,
    /// cache statistics, and backup management.
    /// </summary>
    [RelayCommand]
    private async Task OpenDiagnosticsAsync()
    {
        await _navigationService.OpenDiagnosticsAsync();
    }

    /// <summary>
    /// Opens the edit dialog for the currently selected video.
    /// </summary>
    [RelayCommand]
    private async Task EditVideoAsync()
    {
        if (_videoListVm.SelectedVideo is not VideoEntry video) return;

        try
        {
            await _dialogService.ShowEditDialogAsync(video);

            // Refresh video list after editing to reflect any changes
            if (RefreshCommand.CanExecute(null))
                await RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessage($"无法打开编辑对话框: {ex.Message}", "错误", MessageLevel.Error);
        }
    }

    /// <summary>
    /// Deletes the currently selected video after confirmation.
    /// </summary>
    [RelayCommand]
    private async Task DeleteVideoAsync()
    {
        if (_videoListVm.SelectedVideo is not VideoEntry video) return;

        var confirmResult = await _dialogService.ShowDeleteConfirmAsync(video.Title);
        if (confirmResult is null) return;

        var deleteFile = confirmResult.Value.DeleteFile;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var deleteService = scope.ServiceProvider.GetRequiredService<IDeleteService>();

            var deleteResult = await deleteService.DeleteVideoAsync(video.Id, deleteFile, CancellationToken.None);

            if (deleteResult.Success)
            {
                if (deleteResult.ErrorMessage is not null)
                {
                    _dialogService.ShowMessage(
                        $"视频已从库中移除，但文件删除时出现问题：\n{deleteResult.ErrorMessage}",
                        "部分完成",
                        MessageLevel.Warning);
                }

                if (RefreshCommand.CanExecute(null))
                    await RefreshCommand.ExecuteAsync(null);
            }
            else
            {
                _dialogService.ShowMessage(
                    $"删除失败: {deleteResult.ErrorMessage}",
                    "错误",
                    MessageLevel.Error);
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessage($"删除失败: {ex.Message}", "错误", MessageLevel.Error);
        }
    }
}
