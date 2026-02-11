# 需求文档

## 简介

VideoManager WPF 应用程序的第四轮优化，涵盖七个方面：缩略图加载优先级队列、批量操作分块处理、FFmpeg 熔断器与数据库重试策略、FileWatcher 补偿扫描、全局异常处理增强、MainViewModel 职责拆分、以及代码质量改进（消除魔法字符串 + IAsyncDisposable）。

## 术语表

- **ThumbnailPriorityLoader**: 基于 `Channel<T>` 的缩略图优先级加载服务，负责按可视区域优先顺序调度缩略图加载请求
- **BatchChunkProcessor**: 批量操作分块处理器，将大批量操作拆分为固定大小的块依次执行
- **FFmpegCircuitBreaker**: 基于 Polly `CircuitBreakerStrategy` 的 FFmpeg 调用熔断器
- **DatabaseRetryPolicy**: 基于 Polly 指数退避策略的 SQLite 写操作重试策略
- **CompensationScanner**: FileWatcher 补偿扫描服务，定期全量扫描文件系统并与数据库记录对比
- **GlobalExceptionHandler**: 全局异常处理模块，统一捕获未处理异常并记录日志、展示友好提示
- **PaginationViewModel**: 从 MainViewModel 拆分出的分页职责 ViewModel
- **SortViewModel**: 从 MainViewModel 拆分出的排序职责 ViewModel
- **BatchOperationViewModel**: 从 MainViewModel 拆分出的批量操作职责 ViewModel
- **MetricsOperationNames**: MetricsService 中操作名称的常量集合类
- **VideoListViewModel**: 视频列表 ViewModel，管理分页视频加载、选择跟踪和异步缩略图加载
- **MainViewModel**: 主 ViewModel，协调搜索、分页、刷新、状态文本、导航、对话框管理和批量操作
- **FFmpegService**: FFmpeg 进程调用服务，负责元数据提取和缩略图生成
- **FileWatcherService**: 基于 FileSystemWatcher 的文件监控服务
- **MetricsService**: 性能指标收集服务，包含内存监控、操作计时和缓存统计
- **VideoRepository**: 视频数据访问仓储，提供 CRUD 和分页查询
- **DialogService**: WPF 对话框服务，解耦 ViewModel 与具体 WPF 对话框类型
- **App**: WPF 应用程序入口类（App.xaml.cs）

## 需求

### 需求 1：缩略图加载优先级队列

**用户故事：** 作为用户，我希望当前可视区域内的缩略图优先加载，以便在滚动浏览视频列表时获得更流畅的体验。

#### 验收标准

1. THE ThumbnailPriorityLoader SHALL 使用 `Channel<T>` 实现一个有界优先级加载队列，可视区域内的缩略图请求优先于不可见项的请求被消费
2. WHEN 用户滚动视频列表时，THE ThumbnailPriorityLoader SHALL 取消所有已离开可视区域的待处理缩略图加载请求
3. WHEN 新的缩略图加载请求入队时，THE ThumbnailPriorityLoader SHALL 将可视区域内的请求排列在队列前端
4. WHEN VideoListViewModel 加载新页面时，THE VideoListViewModel SHALL 通过 ThumbnailPriorityLoader 调度缩略图加载，替代当前的顺序逐条加载方式
5. IF ThumbnailPriorityLoader 的后台消费任务发生异常，THEN THE ThumbnailPriorityLoader SHALL 记录错误日志并继续处理后续请求

### 需求 2：批量操作分块处理

**用户故事：** 作为用户，我希望大批量删除、打标签、分类操作不会导致 UI 卡顿或内存飙升，以便在处理大量视频时保持应用响应。

#### 验收标准

1. WHEN 执行批量删除操作时，THE BatchChunkProcessor SHALL 将操作按 50 条一块进行分块处理
2. WHEN 执行批量打标签操作时，THE BatchChunkProcessor SHALL 将操作按 50 条一块进行分块处理
3. WHEN 执行批量分类操作时，THE BatchChunkProcessor SHALL 将操作按 50 条一块进行分块处理
4. WHEN 每个分块处理完成后，THE BatchChunkProcessor SHALL 通过 `Task.Yield()` 让出执行权回 UI 线程，确保 UI 保持响应
5. WHEN 分块处理过程中用户取消操作时，THE BatchChunkProcessor SHALL 在当前块完成后停止处理后续块
6. THE BatchChunkProcessor SHALL 接受可配置的块大小参数，默认值为 50

### 需求 3：FFmpeg 熔断器

**用户故事：** 作为用户，我希望当 FFmpeg 反复失败时系统能自动暂停调用，以避免导入过程被拖慢。

#### 验收标准

1. THE FFmpegCircuitBreaker SHALL 使用 Polly `CircuitBreakerStrategy` 包装 FFmpegService 的 `ExtractMetadataAsync` 和 `GenerateThumbnailAsync` 调用
2. WHEN FFmpegService 在 30 秒窗口内连续失败 5 次时，THE FFmpegCircuitBreaker SHALL 进入 Open 状态，持续 60 秒拒绝所有 FFmpeg 调用
3. WHILE FFmpegCircuitBreaker 处于 Open 状态时，THE FFmpegCircuitBreaker SHALL 立即返回失败结果而不实际调用 FFmpeg 进程
4. WHEN FFmpegCircuitBreaker 从 Open 状态转为 HalfOpen 状态时，THE FFmpegCircuitBreaker SHALL 允许一次试探性调用以检测 FFmpeg 是否恢复
5. WHEN 熔断器状态发生变化时，THE FFmpegCircuitBreaker SHALL 通过日志记录状态转换事件

### 需求 4：数据库重试策略

**用户故事：** 作为用户，我希望 SQLite 写操作在遇到 SQLITE_BUSY 等瞬态错误时能自动重试，以提高数据操作的可靠性。

#### 验收标准

1. THE DatabaseRetryPolicy SHALL 使用 Polly 指数退避策略对关键 SQLite 写操作进行重试，最多重试 3 次，延迟分别为 100ms、200ms、400ms
2. WHEN VideoRepository 的 `AddAsync`、`AddRangeAsync`、`UpdateAsync`、`DeleteAsync` 操作遇到 `Microsoft.Data.Sqlite.SqliteException` 且错误码为 SQLITE_BUSY（5）时，THE DatabaseRetryPolicy SHALL 触发重试
3. IF 所有重试均失败，THEN THE DatabaseRetryPolicy SHALL 将最终异常向上抛出，由调用方处理
4. WHEN 重试发生时，THE DatabaseRetryPolicy SHALL 通过日志记录每次重试的尝试次数和延迟时间

### 需求 5：FileWatcher 补偿扫描

**用户故事：** 作为用户，我希望系统能定期检测文件系统变化，以弥补 FileSystemWatcher 可能丢失事件的问题。

#### 验收标准

1. THE CompensationScanner SHALL 以可配置的间隔（默认每小时一次）执行全量文件系统扫描
2. WHEN CompensationScanner 执行扫描时，THE CompensationScanner SHALL 对比数据库中的视频记录与实际文件系统中的文件
3. WHEN 数据库中存在但文件系统中不存在的视频记录被发现时，THE CompensationScanner SHALL 将对应 VideoEntry 的 `IsFileMissing` 标记为 `true`
4. WHEN 之前标记为 `IsFileMissing` 的视频文件重新出现在文件系统中时，THE CompensationScanner SHALL 将 `IsFileMissing` 恢复为 `false`
5. IF CompensationScanner 扫描过程中发生异常，THEN THE CompensationScanner SHALL 记录错误日志并等待下一次扫描周期
6. THE CompensationScanner SHALL 在 FileWatcherService 中集成，复用现有的文件监控基础设施

### 需求 6：全局异常处理增强

**用户故事：** 作为用户，我希望应用在遇到未处理异常时能展示友好的错误信息而不是崩溃，以获得更稳定的使用体验。

#### 验收标准

1. THE App SHALL 在 `App.xaml.cs` 中注册 `DispatcherUnhandledException` 处理器，捕获 UI 线程未处理异常
2. THE App SHALL 注册 `TaskScheduler.UnobservedTaskException` 处理器，捕获未观察的任务异常
3. WHEN 未处理异常被捕获时，THE GlobalExceptionHandler SHALL 通过 ILogger 记录完整的异常信息（包括堆栈跟踪）
4. WHEN 未处理异常被捕获时，THE GlobalExceptionHandler SHALL 通过 DialogService 向用户展示友好的错误提示信息，隐藏技术细节
5. WHEN DispatcherUnhandledException 被捕获时，THE GlobalExceptionHandler SHALL 将 `args.Handled` 设为 `true` 以防止应用崩溃
6. THE GlobalExceptionHandler SHALL 替换现有的 `MessageBox.Show` 直接调用，统一使用 DialogService 和 ILogger

### 需求 7：MainViewModel 职责拆分

**用户故事：** 作为开发者，我希望 MainViewModel 的职责被合理拆分为独立的 ViewModel，以提高代码的可维护性和可测试性。

#### 验收标准

1. THE PaginationViewModel SHALL 封装分页逻辑，包括当前页、总页数、上一页、下一页、页面信息文本
2. THE SortViewModel SHALL 封装排序逻辑，包括当前排序字段、排序方向、切换排序方向
3. THE BatchOperationViewModel SHALL 封装批量操作逻辑，包括批量删除、批量打标签、批量分类
4. WHEN PaginationViewModel 的分页状态变化时，THE PaginationViewModel SHALL 通过 CommunityToolkit.Mvvm 的 `WeakReferenceMessenger` 发送消息通知 MainViewModel
5. WHEN SortViewModel 的排序条件变化时，THE SortViewModel SHALL 通过 `WeakReferenceMessenger` 发送消息触发视频列表重新加载
6. THE MainViewModel SHALL 保留协调职责，通过 Messenger 接收子 ViewModel 的消息并协调响应
7. WHEN 拆分完成后，THE MainViewModel、PaginationViewModel、SortViewModel、BatchOperationViewModel SHALL 各自可独立进行单元测试

### 需求 8：消除魔法字符串

**用户故事：** 作为开发者，我希望 MetricsService 中的操作名称使用常量统一管理，以避免拼写错误和提高代码可维护性。

#### 验收标准

1. THE MetricsOperationNames SHALL 定义为 `static class`，包含所有 MetricsService 使用的操作名称常量
2. WHEN MetricsService 的 `StartTimer` 被调用时，THE 调用方 SHALL 使用 MetricsOperationNames 中定义的常量而非字符串字面量
3. THE MetricsOperationNames SHALL 至少包含以下常量：`ThumbnailGeneration`、`Import`、`ImportFile`、`Search`、`DatabaseQuery`

### 需求 9：IAsyncDisposable 实现

**用户故事：** 作为开发者，我希望持有异步资源的服务正确实现 `IAsyncDisposable`，以确保资源在应用退出时被正确释放。

#### 验收标准

1. THE MetricsService SHALL 实现 `IAsyncDisposable` 接口，在 `DisposeAsync` 中正确释放 Timer 和其他异步资源
2. THE FileWatcherService SHALL 实现 `IAsyncDisposable` 接口，在 `DisposeAsync` 中正确停止文件监控并释放资源
3. WHEN App 退出时，THE App SHALL 调用所有 `IAsyncDisposable` 服务的 `DisposeAsync` 方法
4. THE MetricsService 和 FileWatcherService SHALL 同时保留现有的 `IDisposable` 实现以保持向后兼容
