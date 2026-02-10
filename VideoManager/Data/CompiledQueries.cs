using Microsoft.EntityFrameworkCore;
using VideoManager.Models;

namespace VideoManager.Data;

/// <summary>
/// Pre-compiled EF Core queries for frequently used query patterns.
/// Compiled queries skip LINQ expression compilation on each execution,
/// improving performance for hot paths like keyword search and default paging.
/// </summary>
public static class CompiledQueries
{
    /// <summary>
    /// Compiled query for keyword search across Title and Description fields.
    /// Results are ordered by ImportedAt descending with skip/take pagination.
    /// Soft-deleted entries are excluded by the global query filter.
    /// </summary>
    public static readonly Func<VideoManagerDbContext, string, int, int, IAsyncEnumerable<VideoEntry>>
        SearchByKeyword = EF.CompileAsyncQuery(
            (VideoManagerDbContext ctx, string keyword, int skip, int take) =>
                ctx.VideoEntries
                    .AsNoTracking()
                    .Include(v => v.Tags)
                    .Include(v => v.Categories)
                    .Where(v => EF.Functions.Like(v.Title, "%" + keyword + "%") ||
                                (v.Description != null && EF.Functions.Like(v.Description, "%" + keyword + "%")))
                    .OrderByDescending(v => v.ImportedAt)
                    .Skip(skip)
                    .Take(take));

    /// <summary>
    /// Compiled query for default paged listing (no filters, ordered by ImportedAt descending).
    /// Soft-deleted entries are excluded by the global query filter.
    /// </summary>
    public static readonly Func<VideoManagerDbContext, int, int, IAsyncEnumerable<VideoEntry>>
        GetPagedDefault = EF.CompileAsyncQuery(
            (VideoManagerDbContext ctx, int skip, int take) =>
                ctx.VideoEntries
                    .AsNoTracking()
                    .Include(v => v.Tags)
                    .Include(v => v.Categories)
                    .OrderByDescending(v => v.ImportedAt)
                    .Skip(skip)
                    .Take(take));
}
