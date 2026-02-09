using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.ViewModels;

/// <summary>
/// ViewModel for the search view. Manages search criteria input,
/// search execution, result display, and pagination.
/// </summary>
public partial class SearchViewModel : ViewModelBase
{
    private readonly ISearchService _searchService;

    /// <summary>
    /// The collection of tag IDs selected for filtering.
    /// </summary>
    public ObservableCollection<int> SelectedTagIds { get; } = new();

    /// <summary>
    /// The collection of search result video entries.
    /// </summary>
    public ObservableCollection<VideoEntry> SearchResults { get; } = new();

    /// <summary>
    /// Keyword for searching in video title and description.
    /// </summary>
    [ObservableProperty]
    private string _keyword = string.Empty;

    /// <summary>
    /// Start date for date range filtering.
    /// </summary>
    [ObservableProperty]
    private DateTime? _dateFrom;

    /// <summary>
    /// End date for date range filtering.
    /// </summary>
    [ObservableProperty]
    private DateTime? _dateTo;

    /// <summary>
    /// Minimum duration for duration range filtering.
    /// </summary>
    [ObservableProperty]
    private TimeSpan? _durationMin;

    /// <summary>
    /// Maximum duration for duration range filtering.
    /// </summary>
    [ObservableProperty]
    private TimeSpan? _durationMax;

    /// <summary>
    /// The current page number (1-based).
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private int _currentPage = 1;

    /// <summary>
    /// The total number of pages available.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private int _totalPages;

    /// <summary>
    /// The total number of matching results.
    /// </summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>
    /// The number of results per page.
    /// </summary>
    [ObservableProperty]
    private int _pageSize = 20;

    /// <summary>
    /// Whether a search is currently in progress.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private bool _isSearching;

    /// <summary>
    /// The currently selected video entry from search results.
    /// </summary>
    [ObservableProperty]
    private VideoEntry? _selectedVideo;

    /// <summary>
    /// Creates a new SearchViewModel.
    /// </summary>
    /// <param name="searchService">The search service for executing searches.</param>
    public SearchViewModel(ISearchService searchService)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
    }

    /// <summary>
    /// Executes a search with the current criteria, resetting to page 1.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync(CancellationToken ct = default)
    {
        CurrentPage = 1;
        await ExecuteSearchAsync(ct);
    }

    private bool CanSearch() => !IsSearching;

    /// <summary>
    /// Navigates to the next page of search results.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoNextPage))]
    private async Task NextPageAsync(CancellationToken ct = default)
    {
        CurrentPage++;
        await ExecuteSearchAsync(ct);
    }

    private bool CanGoNextPage() => !IsSearching && CurrentPage < TotalPages;

    /// <summary>
    /// Navigates to the previous page of search results.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoPreviousPage))]
    private async Task PreviousPageAsync(CancellationToken ct = default)
    {
        CurrentPage--;
        await ExecuteSearchAsync(ct);
    }

    private bool CanGoPreviousPage() => !IsSearching && CurrentPage > 1;

    /// <summary>
    /// Clears all search criteria and results.
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
    {
        Keyword = string.Empty;
        SelectedTagIds.Clear();
        DateFrom = null;
        DateTo = null;
        DurationMin = null;
        DurationMax = null;
        SearchResults.Clear();
        CurrentPage = 1;
        TotalPages = 0;
        TotalCount = 0;
        SelectedVideo = null;
    }

    /// <summary>
    /// Executes the search with current criteria and current page.
    /// </summary>
    private async Task ExecuteSearchAsync(CancellationToken ct)
    {
        IsSearching = true;
        try
        {
            var criteria = BuildSearchCriteria();
            var result = await _searchService.SearchAsync(criteria, CurrentPage, PageSize, ct);

            SearchResults.Clear();
            foreach (var item in result.Items)
            {
                SearchResults.Add(item);
            }

            TotalCount = result.TotalCount;
            TotalPages = CalculateTotalPages(result.TotalCount, PageSize);

            // Ensure CurrentPage is valid after loading
            if (CurrentPage > TotalPages && TotalPages > 0)
            {
                CurrentPage = TotalPages;
            }
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>
    /// Builds a SearchCriteria from the current filter properties.
    /// </summary>
    internal SearchCriteria BuildSearchCriteria()
    {
        return new SearchCriteria(
            Keyword: string.IsNullOrWhiteSpace(Keyword) ? null : Keyword,
            TagIds: SelectedTagIds.Count > 0 ? SelectedTagIds.ToList() : null,
            DateFrom: DateFrom,
            DateTo: DateTo,
            DurationMin: DurationMin,
            DurationMax: DurationMax
        );
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
