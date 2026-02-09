using VideoManager.Models;

namespace VideoManager.Services;

public interface ISearchService
{
    Task<PagedResult<VideoEntry>> SearchAsync(SearchCriteria criteria, int page, int pageSize, CancellationToken ct);
}
