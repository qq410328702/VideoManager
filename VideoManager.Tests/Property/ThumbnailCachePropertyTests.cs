using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for thumbnail cache idempotency.
/// Tests Property 9: 缩略图缓存幂等性
///
/// **Feature: video-manager-optimization, Property 9: 缩略图缓存幂等性**
/// **Validates: Requirements 7.2**
///
/// For any valid thumbnail path, two consecutive calls to LoadThumbnailAsync
/// should return the same result, and the second call should return from
/// memory cache rather than re-reading disk (fileExistsCheck called only once).
/// </summary>
public class ThumbnailCachePropertyTests
{
    private static IOptions<VideoManagerOptions> CreateOptions(int cacheMaxSize = 1000)
    {
        return Options.Create(new VideoManagerOptions { ThumbnailCacheMaxSize = cacheMaxSize });
    }

    /// <summary>
    /// Generates a thumbnail cache test scenario as an int array:
    /// [pathSeed, fileExists]
    /// pathSeed: positive int used to generate a unique thumbnail path
    /// fileExists: 0 or 1 indicating whether the file exists on disk
    /// </summary>
    private static FsCheck.Arbitrary<int[]> ThumbnailCacheScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var pathSeed = arr.Length > 0 ? Math.Abs(arr[0]) + 1 : 1;
                var fileExists = arr.Length > 1 ? arr[1] % 2 : 0;
                return new int[] { pathSeed, fileExists };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 2));
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 9: 缩略图缓存幂等性**
    /// **Validates: Requirements 7.2**
    ///
    /// For any valid thumbnail path and file existence state, two consecutive calls
    /// to LoadThumbnailAsync should:
    /// 1. Return the same result (idempotency)
    /// 2. Only invoke the fileExistsCheck once (second call served from cache)
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property LoadThumbnailAsync_ConsecutiveCalls_ShouldBeIdempotentAndCached()
    {
        return FsCheck.Fluent.Prop.ForAll(ThumbnailCacheScenarioArb(), config =>
        {
            int pathSeed = config[0];
            bool fileExists = config[1] != 0;

            var thumbnailPath = $"/thumbnails/thumb_{pathSeed}.jpg";

            // Track how many times fileExistsCheck is called
            var callCount = 0;
            var service = new ThumbnailCacheService(_ =>
            {
                Interlocked.Increment(ref callCount);
                return fileExists;
            }, CreateOptions(), NullLogger<ThumbnailCacheService>.Instance);

            // First call
            var firstResult = service.LoadThumbnailAsync(thumbnailPath)
                .GetAwaiter().GetResult();

            // Second call
            var secondResult = service.LoadThumbnailAsync(thumbnailPath)
                .GetAwaiter().GetResult();

            // Property 1: Both calls return the same result (idempotency)
            var sameResult = firstResult == secondResult;

            // Property 2: fileExistsCheck was called exactly once (cache hit on second call)
            var cachedOnSecondCall = callCount == 1;

            // Property 3: Result correctness — path returned when file exists, null otherwise
            var correctResult = fileExists
                ? firstResult == thumbnailPath
                : firstResult == null;

            return sameResult && cachedOnSecondCall && correctResult;
        });
    }
}
