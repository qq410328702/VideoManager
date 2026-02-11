using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace VideoManager.ViewModels;

/// <summary>
/// ViewModel responsible for pagination logic, extracted from MainViewModel.
/// Manages current page, total pages, page info text, and page navigation commands.
/// Communicates page changes via WeakReferenceMessenger.
/// </summary>
public partial class PaginationViewModel : ViewModelBase
{
    private readonly VideoListViewModel _videoListVm;

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    [ObservableProperty]
    private int _currentPage = 1;

    /// <summary>
    /// Total number of pages.
    /// </summary>
    [ObservableProperty]
    private int _totalPages;

    /// <summary>
    /// Total number of video items across all pages.
    /// </summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>
    /// Page info text displayed in the UI (e.g. "第 1/5 页 (共 100 个)").
    /// </summary>
    [ObservableProperty]
    private string _pageInfoText = "第 1 页";

    public PaginationViewModel(VideoListViewModel videoListVm)
    {
        _videoListVm = videoListVm ?? throw new ArgumentNullException(nameof(videoListVm));

        // Sync initial state from VideoListViewModel
        SyncFromVideoListVm();

        // Subscribe to VideoListViewModel property changes for page info updates
        _videoListVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(VideoListViewModel.CurrentPage)
                or nameof(VideoListViewModel.TotalPages)
                or nameof(VideoListViewModel.TotalCount))
            {
                SyncFromVideoListVm();
            }
        };
    }

    /// <summary>
    /// Navigates to the previous page.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoPreviousPage))]
    private async Task PreviousPageAsync()
    {
        if (_videoListVm.PreviousPageCommand.CanExecute(null))
        {
            await _videoListVm.PreviousPageCommand.ExecuteAsync(null);
            SyncFromVideoListVm();
            WeakReferenceMessenger.Default.Send(new PageChangedMessage(CurrentPage));
        }
    }

    private bool CanGoPreviousPage() => CurrentPage > 1;

    /// <summary>
    /// Navigates to the next page.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoNextPage))]
    private async Task NextPageAsync()
    {
        if (_videoListVm.NextPageCommand.CanExecute(null))
        {
            await _videoListVm.NextPageCommand.ExecuteAsync(null);
            SyncFromVideoListVm();
            WeakReferenceMessenger.Default.Send(new PageChangedMessage(CurrentPage));
        }
    }

    private bool CanGoNextPage() => CurrentPage < TotalPages;

    /// <summary>
    /// Synchronizes pagination state from VideoListViewModel and updates page info text.
    /// Called when VideoListViewModel's pagination properties change.
    /// </summary>
    public void SyncFromVideoListVm()
    {
        CurrentPage = _videoListVm.CurrentPage;
        TotalPages = _videoListVm.TotalPages;
        TotalCount = _videoListVm.TotalCount;
        UpdatePageInfo();

        // Notify command CanExecute re-evaluation
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Updates the page info text and sends a PageInfoUpdatedMessage.
    /// </summary>
    private void UpdatePageInfo()
    {
        PageInfoText = $"第 {CurrentPage}/{TotalPages} 页 (共 {TotalCount} 个)";
        WeakReferenceMessenger.Default.Send(
            new PageInfoUpdatedMessage(CurrentPage, TotalPages, TotalCount));
    }
}
