# 任务列表：视频管理器优化 V2

## 任务

- [x] 1. 数据库查询优化 — AsNoTracking
  - [x] 1.1 在 `SearchService.SearchAsync` 的查询链中添加 `.AsNoTracking()`，位于 `.Include()` 之后
  - [x] 1.2 编写单元测试 `SearchServiceAsNoTrackingTests.cs`，验证 AsNoTracking 查询返回正确结果且包含 Tags/Categories
  - [x] 1.3 编写属性测试 `AsNoTrackingPropertyTests.cs`（Property 1：AsNoTracking 查询结果一致性）

- [x] 2. 结构化日志
  - [x] 2.1 在 `App.xaml.cs` 的 `ConfigureServices` 中添加 `services.AddLogging(builder => { builder.AddDebug(); })` 注册日志服务
  - [x] 2.2 为 `ImportService` 添加 `ILogger<ImportService>` 构造函数参数，替换元数据提取失败和缩略图生成失败处的静默 catch 为 `_logger.LogWarning`
  - [x] 2.3 为 `DeleteService` 添加 `ILogger<DeleteService>` 构造函数参数，在删除操作和文件删除失败处添加日志
  - [x] 2.4 为 `SearchService` 添加 `ILogger<SearchService>` 构造函数参数，在搜索执行处添加 Debug 级别日志
  - [x] 2.5 为 `EditService` 添加 `ILogger<EditService>` 构造函数参数，在编辑操作处添加 Information 级别日志
  - [x] 2.6 为 `FileWatcherService` 添加 `ILogger<FileWatcherService>` 构造函数参数，替换现有 `Trace.TraceError` 调用
  - [x] 2.7 为 `ThumbnailCacheService` 和 `WindowSettingsService` 添加 `ILogger<T>` 构造函数参数，添加适当级别日志
  - [x] 2.8 更新所有受影响 Service 的现有单元测试，在构造函数中传入 `Mock<ILogger<T>>` 或 `NullLogger<T>`
  - [x] 2.9 编写单元测试 `LoggingIntegrationTests.cs`，验证关键场景下日志方法被调用（通过 Mock 验证）

- [x] 3. 输入验证统一
  - [x] 3.1 在 `VideoEntry` 模型的 `Title` 属性上添加 `[Required]` 和 `[StringLength(500)]`，`FileName` 属性上添加 `[Required]`
  - [x] 3.2 在 `Tag` 模型的 `Name` 属性上添加 `[Required]` 和 `[StringLength(100)]`，`Color` 属性上添加 `[StringLength(9)]`
  - [x] 3.3 创建 `VideoManager/Services/ValidationHelper.cs` 静态工具类，实现 `ValidateEntity` 方法（使用 `System.ComponentModel.DataAnnotations.Validator`）
  - [x] 3.4 在 `EditService.UpdateVideoInfoAsync` 和 `ImportService` 的实体创建处调用 `ValidationHelper.ValidateEntity` 进行写入前验证
  - [x] 3.5 编写单元测试 `ValidationHelperTests.cs`，验证有效/无效输入的验证行为
  - [x] 3.6 编写属性测试 `ValidationPropertyTests.cs`（Property 2：Data Annotations 验证正确性）

- [x] 4. 瞬态故障重试
  - [x] 4.1 在 `VideoManager.csproj` 中添加 `Polly.Core` NuGet 包引用
  - [x] 4.2 在 `ImportService` 中创建 Polly `ResiliencePipeline`（最多重试 2 次，线性退避 1s/2s，排除 OperationCanceledException）
  - [x] 4.3 将 `ProcessFileMetadataAsync` 中的 FFmpeg `ExtractMetadataAsync` 调用包装在重试管道中
  - [x] 4.4 将 `ProcessFileMetadataAsync` 中的 FFmpeg `GenerateThumbnailAsync` 调用包装在重试管道中
  - [x] 4.5 编写单元测试 `ImportServiceRetryTests.cs`，验证重试行为（成功重试、重试耗尽、取消不重试）
  - [x] 4.6 编写属性测试 `RetryPolicyPropertyTests.cs`（Property 3：重试策略行为）

- [x] 5. Repository 批量操作
  - [x] 5.1 在 `IVideoRepository` 接口中添加 `Task AddRangeAsync(IEnumerable<VideoEntry> entries, CancellationToken ct)` 方法
  - [x] 5.2 在 `VideoRepository` 中实现 `AddRangeAsync`（使用 `_context.VideoEntries.AddRange` + 单次 `SaveChangesAsync`）
  - [x] 5.3 重构 `ImportService.ImportVideosAsync`：Phase 2 中收集成功的 VideoEntry 到列表，Phase 2 结束后使用 `AddRangeAsync` 批量写入，失败时回退到逐条 `AddAsync`
  - [x] 5.4 编写单元测试 `VideoRepositoryBatchTests.cs`，验证 AddRangeAsync 正确写入多条记录
  - [x] 5.5 编写属性测试 `BatchWritePropertyTests.cs`（Property 4：批量写入一致性）

- [x] 6. 软删除机制
  - [x] 6.1 在 `VideoEntry` 模型中添加 `IsDeleted`（bool，默认 false）和 `DeletedAt`（DateTime?，默认 null）属性
  - [x] 6.2 在 `VideoManagerDbContext.OnModelCreating` 中添加全局查询过滤器 `modelBuilder.Entity<VideoEntry>().HasQueryFilter(v => !v.IsDeleted)`
  - [x] 6.3 创建 EF Core 迁移添加 `IsDeleted` 和 `DeletedAt` 列（或在 App.xaml.cs 的 fallback 逻辑中添加 ALTER TABLE）
  - [x] 6.4 修改 `DeleteService.DeleteVideoAsync`：将物理删除改为软删除（设置 IsDeleted=true, DeletedAt=DateTime.UtcNow），保留 Tags/Categories 清除逻辑
  - [x] 6.5 修改 `DeleteService.BatchDeleteAsync`：确保批量删除也使用软删除逻辑
  - [x] 6.6 编写单元测试 `SoftDeleteServiceTests.cs`，验证软删除行为（标记设置、文件删除、关联清除）
  - [x] 6.7 编写属性测试 `SoftDeleteFilterPropertyTests.cs`（Property 5：软删除全局过滤器）
  - [x] 6.8 编写属性测试 `SoftDeleteStatePropertyTests.cs`（Property 6：软删除状态设置）
