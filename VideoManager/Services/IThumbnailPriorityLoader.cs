namespace VideoManager.Services;

/// <summary>
/// 基于 Channel&lt;T&gt; 的缩略图优先级加载服务。
/// 可视区域内的请求优先处理，滚动时取消不可见项的请求。
/// </summary>
public interface IThumbnailPriorityLoader : IAsyncDisposable
{
    /// <summary>
    /// 将缩略图加载请求入队。
    /// </summary>
    /// <param name="videoId">视频 ID</param>
    /// <param name="thumbnailPath">缩略图文件路径</param>
    /// <param name="isVisible">是否在当前可视区域内</param>
    void Enqueue(int videoId, string thumbnailPath, bool isVisible);

    /// <summary>
    /// 更新可视区域，取消不可见项的待处理请求。
    /// </summary>
    /// <param name="visibleVideoIds">当前可视区域内的视频 ID 集合</param>
    void UpdateVisibleItems(IReadOnlySet<int> visibleVideoIds);

    /// <summary>
    /// 缩略图加载完成事件。
    /// </summary>
    event Action<int, string?>? ThumbnailLoaded;
}
