using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.ViewModels;

/// <summary>
/// Main ViewModel that coordinates top-level application logic:
/// search triggering, pagination control, refresh, status text updates,
/// navigation, dialog management, and batch operations.
/// All business logic is handled here; MainWindow code-behind only retains
/// window lifecycle management, DataContext binding, and keyboard shortcuts.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly VideoListViewModel _videoListVm;
    private readonly SearchViewModel _searchVm;
    private readonly CategoryViewModel _categoryVm;
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
    /// Creates a new MainViewModel with injected child ViewModels, services, and FileWatcher service.
    /// </summary>
    public MainViewModel(
        VideoListViewModel videoListVm,
        SearchViewModel searchVm,
        CategoryViewModel categoryVm,
        IFileWatcherService fileWatcherService,
        IOptions<VideoManagerOptions> options,
        INavigationService navigationService,
        IDialogService dialogService,
        IServiceProvider serviceProvider)
    {
        _videoListVm = videoListVm ?? throw new ArgumentNullException(nameof(videoListVm));
        _searchVm = searchVm ?? throw new ArgumentNullException(nameof(searchVm));
        _categoryVm = categoryVm ?? throw new ArgumentNullException(nameof(categoryVm));
        _fileWatcherService = fileWatcherService ?? throw new ArgumentNullException(nameof(fileWatcherService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

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

    // ==================== Navigation & Dialog Commands (migrated from MainWindow code-behind) ====================

    /// <summary>
    /// Opens the video player for the currently selected video.
    /// Migrated from MainWindow.VideoListControl_VideoDoubleClicked (Req 11.3).
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
    /// Migrated from MainWindow.ImportButton_Click (Req 11.3).
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
    /// cache statistics, and backup management (Req 14.4).
    /// </summary>
    [RelayCommand]
    private async Task OpenDiagnosticsAsync()
    {
        await _navigationService.OpenDiagnosticsAsync();
    }

    /// <summary>
    /// Opens the edit dialog for the currently selected video.
    /// Migrated from MainWindow.VideoListControl_EditVideoRequested (Req 12.6).
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
    /// Migrated from MainWindow.VideoListControl_DeleteVideoRequested (Req 12.6).
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

    /// <summary>
    /// Batch deletes selected videos after confirmation.
    /// Creates a backup before executing the batch delete (Req 8.3).
    /// Migrated from MainWindow.VideoListControl_BatchDeleteRequested (Req 12.6).
    /// </summary>
    [RelayCommand]
    private async Task BatchDeleteAsync()
    {
        var videoListVm = _videoListVm;
        var selectedIds = videoListVm.GetSelectedVideoIds();
        if (selectedIds.Count == 0) return;

        var confirmResult = await _dialogService.ShowBatchDeleteConfirmAsync(selectedIds.Count);
        if (confirmResult is null) return;

        var deleteFiles = confirmResult.Value.DeleteFile;

        var ct = videoListVm.BeginBatchOperation();
        videoListVm.BatchProgressText = "正在批量删除...";

        try
        {
            // Create backup before batch delete (Req 8.3)
            try
            {
                var backupService = _serviceProvider.GetRequiredService<IBackupService>();
                videoListVm.BatchProgressText = "正在创建备份...";
                await backupService.CreateBackupAsync(CancellationToken.None);
            }
            catch (Exception backupEx)
            {
                // Log but don't block the delete operation
                Trace.TraceWarning($"Pre-delete backup failed: {backupEx.Message}");
            }

            videoListVm.BatchProgressText = "正在批量删除...";

            using var scope = _serviceProvider.CreateScope();
            var deleteService = scope.ServiceProvider.GetRequiredService<IDeleteService>();

            var estimator = new ProgressEstimator(selectedIds.Count);
            var lastReportedCompleted = 0;

            var progress = new Progress<BatchProgress>(p =>
            {
                // Record completions in the estimator for each newly completed item
                var newlyCompleted = p.Completed - lastReportedCompleted;
                for (var i = 0; i < newlyCompleted; i++)
                {
                    estimator.RecordCompletion();
                }
                lastReportedCompleted = p.Completed;

                videoListVm.BatchProgressPercentage = estimator.ProgressPercentage;
                videoListVm.BatchProgressText = $"正在删除... ({p.Completed}/{p.Total})";
                videoListVm.BatchEstimatedTimeRemaining = VideoListViewModel.FormatTimeRemaining(estimator.EstimatedTimeRemaining);
            });

            var deleteResult = await deleteService.BatchDeleteAsync(selectedIds, deleteFiles, progress, ct);

            // Show result summary (Req 6.5)
            var summaryMessage = $"批量删除完成\n\n成功: {deleteResult.SuccessCount} 个\n失败: {deleteResult.FailCount} 个";
            if (deleteResult.Errors.Count > 0)
            {
                summaryMessage += "\n\n失败详情:\n" + string.Join("\n",
                    deleteResult.Errors.Select(err => $"  视频 {err.VideoId}: {err.Reason}"));
            }

            _dialogService.ShowMessage(summaryMessage, "批量删除结果",
                deleteResult.FailCount > 0 ? MessageLevel.Warning : MessageLevel.Information);

            // Refresh video list after deletion
            if (RefreshCommand.CanExecute(null))
                await RefreshCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            _dialogService.ShowMessage("批量删除已取消。已完成的删除操作不会回滚。", "已取消", MessageLevel.Information);

            // Refresh to reflect any items that were already deleted
            if (RefreshCommand.CanExecute(null))
                await RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessage($"批量删除失败: {ex.Message}", "错误", MessageLevel.Error);
        }
        finally
        {
            videoListVm.EndBatchOperation();
        }
    }

    /// <summary>
    /// Batch adds tags to selected videos.
    /// Migrated from MainWindow.VideoListControl_BatchTagRequested (Req 12.6).
    /// </summary>
    [RelayCommand]
    private async Task BatchTagAsync()
    {
        var videoListVm = _videoListVm;
        var selectedIds = videoListVm.GetSelectedVideoIds();
        if (selectedIds.Count == 0) return;

        var availableTags = _categoryVm.Tags;
        if (availableTags.Count == 0)
        {
            _dialogService.ShowMessage("没有可用的标签。请先在分类面板中创建标签。", "提示", MessageLevel.Information);
            return;
        }

        var selectedTags = await _dialogService.ShowBatchTagDialogAsync(availableTags, selectedIds.Count);
        if (selectedTags is null) return;

        var ct = videoListVm.BeginBatchOperation();
        videoListVm.BatchProgressText = "正在批量添加标签...";

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var editService = scope.ServiceProvider.GetRequiredService<IEditService>();

            var totalOps = selectedTags.Count;
            var estimator = new ProgressEstimator(totalOps);
            var completedOps = 0;

            foreach (var tag in selectedTags)
            {
                ct.ThrowIfCancellationRequested();

                await editService.BatchAddTagAsync(selectedIds, tag.Id, ct);
                completedOps++;
                estimator.RecordCompletion();

                videoListVm.BatchProgressPercentage = estimator.ProgressPercentage;
                videoListVm.BatchProgressText = $"正在添加标签... ({completedOps}/{totalOps})";
                videoListVm.BatchEstimatedTimeRemaining = VideoListViewModel.FormatTimeRemaining(estimator.EstimatedTimeRemaining);
            }

            // Show result summary (Req 6.5)
            _dialogService.ShowMessage(
                $"批量标签添加完成\n\n已为 {selectedIds.Count} 个视频添加了 {selectedTags.Count} 个标签",
                "批量标签结果",
                MessageLevel.Information);

            // Refresh video list
            if (RefreshCommand.CanExecute(null))
                await RefreshCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            _dialogService.ShowMessage("批量标签添加已取消。已完成的标签操作不会回滚。", "已取消", MessageLevel.Information);

            // Refresh to reflect any tags that were already added
            if (RefreshCommand.CanExecute(null))
                await RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessage($"批量标签添加失败: {ex.Message}", "错误", MessageLevel.Error);
        }
        finally
        {
            videoListVm.EndBatchOperation();
        }
    }

    /// <summary>
    /// Batch moves selected videos to a category.
    /// Migrated from MainWindow.VideoListControl_BatchCategoryRequested (Req 12.6).
    /// </summary>
    [RelayCommand]
    private async Task BatchCategoryAsync()
    {
        var videoListVm = _videoListVm;
        var selectedIds = videoListVm.GetSelectedVideoIds();
        if (selectedIds.Count == 0) return;

        var categories = _categoryVm.Categories;
        if (categories.Count == 0)
        {
            _dialogService.ShowMessage("没有可用的分类。请先在分类面板中创建分类。", "提示", MessageLevel.Information);
            return;
        }

        var selectedCategory = await _dialogService.ShowBatchCategoryDialogAsync(categories, selectedIds.Count);
        if (selectedCategory is null) return;

        var ct = videoListVm.BeginBatchOperation();
        videoListVm.BatchProgressText = "正在批量移动到分类...";

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var editService = scope.ServiceProvider.GetRequiredService<IEditService>();

            // BatchMoveToCategoryAsync is a single operation, but we still use ProgressEstimator
            // to show progress for the overall operation
            var estimator = new ProgressEstimator(1);

            await editService.BatchMoveToCategoryAsync(selectedIds, selectedCategory.Id, ct);

            estimator.RecordCompletion();
            videoListVm.BatchProgressPercentage = 100;

            // Show result summary (Req 6.5)
            _dialogService.ShowMessage(
                $"批量分类移动完成\n\n已将 {selectedIds.Count} 个视频移动到分类 \"{selectedCategory.Name}\"",
                "批量分类结果",
                MessageLevel.Information);

            // Refresh video list
            if (RefreshCommand.CanExecute(null))
                await RefreshCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            _dialogService.ShowMessage("批量分类移动已取消。已完成的分类操作不会回滚。", "已取消", MessageLevel.Information);

            // Refresh to reflect any changes that were already made
            if (RefreshCommand.CanExecute(null))
                await RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessage($"批量分类移动失败: {ex.Message}", "错误", MessageLevel.Error);
        }
        finally
        {
            videoListVm.EndBatchOperation();
        }
    }
}
