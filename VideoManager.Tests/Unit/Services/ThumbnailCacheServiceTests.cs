using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

public class ThumbnailCacheServiceTests
{
    #region LoadThumbnailAsync — File exists

    [Fact]
    public async Task LoadThumbnailAsync_FileExists_ReturnsPath()
    {
        var service = new ThumbnailCacheService(_ => true);

        var result = await service.LoadThumbnailAsync("/thumbnails/video1.jpg");

        Assert.Equal("/thumbnails/video1.jpg", result);
    }

    #endregion

    #region LoadThumbnailAsync — File does not exist

    [Fact]
    public async Task LoadThumbnailAsync_FileDoesNotExist_ReturnsNull()
    {
        var service = new ThumbnailCacheService(_ => false);

        var result = await service.LoadThumbnailAsync("/thumbnails/missing.jpg");

        Assert.Null(result);
    }

    #endregion

    #region LoadThumbnailAsync — Cache hit returns cached value

    [Fact]
    public async Task LoadThumbnailAsync_SecondCall_ReturnsCachedValue()
    {
        var callCount = 0;
        var service = new ThumbnailCacheService(_ =>
        {
            callCount++;
            return true;
        });

        var first = await service.LoadThumbnailAsync("/thumbnails/video1.jpg");
        var second = await service.LoadThumbnailAsync("/thumbnails/video1.jpg");

        Assert.Equal("/thumbnails/video1.jpg", first);
        Assert.Equal("/thumbnails/video1.jpg", second);
        Assert.Equal(1, callCount); // File.Exists called only once
    }

    [Fact]
    public async Task LoadThumbnailAsync_CachesNullForMissingFile()
    {
        var callCount = 0;
        var service = new ThumbnailCacheService(_ =>
        {
            callCount++;
            return false;
        });

        var first = await service.LoadThumbnailAsync("/thumbnails/missing.jpg");
        var second = await service.LoadThumbnailAsync("/thumbnails/missing.jpg");

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, callCount); // File.Exists called only once
    }

    #endregion

    #region LoadThumbnailAsync — Different paths cached independently

    [Fact]
    public async Task LoadThumbnailAsync_DifferentPaths_CachedIndependently()
    {
        var service = new ThumbnailCacheService(path => path.Contains("exists"));

        var existing = await service.LoadThumbnailAsync("/thumbnails/exists.jpg");
        var missing = await service.LoadThumbnailAsync("/thumbnails/gone.jpg");

        Assert.Equal("/thumbnails/exists.jpg", existing);
        Assert.Null(missing);
    }

    #endregion

    #region LoadThumbnailAsync — Null argument

    [Fact]
    public async Task LoadThumbnailAsync_NullPath_ThrowsArgumentNullException()
    {
        var service = new ThumbnailCacheService(_ => true);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.LoadThumbnailAsync(null!));
    }

    #endregion

    #region ClearCache

    [Fact]
    public async Task ClearCache_SubsequentCallChecksFileAgain()
    {
        var callCount = 0;
        var service = new ThumbnailCacheService(_ =>
        {
            callCount++;
            return true;
        });

        await service.LoadThumbnailAsync("/thumbnails/video1.jpg");
        Assert.Equal(1, callCount);

        service.ClearCache();

        await service.LoadThumbnailAsync("/thumbnails/video1.jpg");
        Assert.Equal(2, callCount); // File.Exists called again after cache clear
    }

    [Fact]
    public async Task ClearCache_ClearsAllEntries()
    {
        var callCount = 0;
        var service = new ThumbnailCacheService(_ =>
        {
            callCount++;
            return true;
        });

        await service.LoadThumbnailAsync("/thumbnails/a.jpg");
        await service.LoadThumbnailAsync("/thumbnails/b.jpg");
        Assert.Equal(2, callCount);

        service.ClearCache();

        await service.LoadThumbnailAsync("/thumbnails/a.jpg");
        await service.LoadThumbnailAsync("/thumbnails/b.jpg");
        Assert.Equal(4, callCount); // Both re-checked after clear
    }

    #endregion

    #region Constructor — Null argument

    [Fact]
    public void Constructor_NullFileExistsCheck_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ThumbnailCacheService(null!));
    }

    #endregion
}
