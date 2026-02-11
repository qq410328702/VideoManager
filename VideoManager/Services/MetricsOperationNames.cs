namespace VideoManager.Services;

/// <summary>
/// MetricsService 操作名称常量集合。
/// 消除魔法字符串，统一管理所有 StartTimer 操作名称。
/// </summary>
public static class MetricsOperationNames
{
    public const string ThumbnailGeneration = "thumbnail_generation";
    public const string Import = "import";
    public const string ImportFile = "import_file";
    public const string Search = "search";
    public const string DatabaseQuery = "database_query";
    public const string BatchDelete = "batch_delete";
    public const string BatchTag = "batch_tag";
    public const string BatchCategory = "batch_category";
    public const string CompensationScan = "compensation_scan";
}
