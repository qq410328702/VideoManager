# 实现计划: VideoManager 第四轮优化

## 概述

基于设计文档，将七个优化领域拆分为增量式编码任务。每个任务构建在前一个任务之上，最终通过集成步骤将所有组件连接起来。测试框架使用 xUnit + FsCheck。

## 任务

- [x] 1. MetricsOperationNames 常量类 + 消除魔法字符串
  - [x] 1.1 创建 `VideoManager/Services/MetricsOperationNames.cs` 静态类，定义所有操作名称常量（ThumbnailGeneration、Import、ImportFile、Search、DatabaseQuery、BatchDelete、BatchTag、BatchCategory、CompensationScan）
    - _Requirements: 8.1, 8.3_
  - [x] 1.2 将 FFmpegService、ImportService 及其他服务中的魔法字符串替换为 MetricsOperationNames 常量引用
    - _Requirements: 8.2_
  - [x] 1.3 编写单元测试验证 MetricsOperationNames 包含所有必需常量
    - _Requirements: 8.3_

- [x] 2. BatchChunkProcessor 分块处理器
  - [x] 2.1 创建 `VideoManager/Services/BatchChunkProcessor.cs` 静态类，实现 `ProcessInChunksAsync<T>` 方法，支持可配置块大小、Task.Yield()、取消令牌和进度报告
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_
  - [x] 2.2 编写属性测试验证批量分块正确性
    - **Property 4: 批量分块正确性**
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.6**
  - [x] 2.3 编写属性测试验证批量分块取消
    - **Property 5: 批量分块取消**
    - **Validates: Requirements 2.5**
  - [x] 2.4 编写单元测试覆盖边界情况（空列表、块大小大于列表长度）
    - _Requirements: 2.1, 2.6_

- [x] 3. 检查点 - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 4. DatabaseRetryPolicy 数据库重试策略
  - [x] 4.1 创建 `VideoManager/Services/DatabaseRetryPolicy.cs` 静态类，实现 `CreateRetryPipeline` 方法，配置 Polly 指数退避策略（3次重试，100ms/200ms/400ms），仅处理 SqliteException 错误码 5
    - _Requirements: 4.1, 4.2, 4.3_
  - [x] 4.2 在 `VideoRepository` 的 `AddAsync`、`AddRangeAsync`、`UpdateAsync`、`DeleteAsync` 方法中集成 DatabaseRetryPolicy，包装 `SaveChangesAsync` 调用
    - _Requirements: 4.1, 4.2, 4.4_
  - [x] 4.3 编写属性测试验证数据库重试仅针对 SQLITE_BUSY
    - **Property 7: 数据库重试仅针对 SQLITE_BUSY**
    - **Validates: Requirements 4.1, 4.2**
  - [x] 4.4 编写单元测试覆盖所有重试耗尽后异常抛出的情况
    - _Requirements: 4.3_

- [x] 5. ResilientFFmpegService 熔断器装饰器
  - [x] 5.1 创建 `VideoManager/Services/ResilientFFmpegService.cs`，实现 IFFmpegService 接口，使用 Polly CircuitBreakerStrategy 包装内部 FFmpegService 调用（30秒窗口/5次失败/60秒熔断）
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_
  - [x] 5.2 在 `App.xaml.cs` 的 DI 配置中，将 FFmpegService 注册替换为 ResilientFFmpegService 装饰器模式
    - _Requirements: 3.1_
  - [x] 5.3 编写属性测试验证熔断器连续失败后开启
    - **Property 6: 熔断器连续失败后开启**
    - **Validates: Requirements 3.2, 3.3**
  - [x] 5.4 编写单元测试覆盖 HalfOpen 试探性调用和状态日志记录
    - _Requirements: 3.4, 3.5_

- [x] 6. 检查点 - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 7. ThumbnailPriorityLoader 缩略图优先级加载
  - [x] 7.1 创建 `VideoManager/Services/IThumbnailPriorityLoader.cs` 接口和 `VideoManager/Services/ThumbnailPriorityLoader.cs` 实现，使用 Channel<T> 实现优先级队列，后台消费者 Task 优先处理可见项，支持 UpdateVisibleItems 取消不可见请求
    - _Requirements: 1.1, 1.2, 1.3, 1.5_
  - [x] 7.2 修改 `VideoListViewModel`，将现有的顺序缩略图加载逻辑替换为通过 ThumbnailPriorityLoader 调度
    - _Requirements: 1.4_
  - [x] 7.3 在 `App.xaml.cs` 的 DI 配置中注册 IThumbnailPriorityLoader 为 Singleton
    - _Requirements: 1.1_
  - [x] 7.4 编写属性测试验证缩略图优先级排序
    - **Property 1: 缩略图优先级排序**
    - **Validates: Requirements 1.1, 1.3**
  - [x] 7.5 编写属性测试验证滚动时取消不可见请求
    - **Property 2: 滚动时取消不可见请求**
    - **Validates: Requirements 1.2**
  - [x] 7.6 编写属性测试验证缩略图加载错误恢复
    - **Property 3: 缩略图加载错误恢复**
    - **Validates: Requirements 1.5**

- [x] 8. CompensationScanner 补偿扫描
  - [x] 8.1 扩展 `IFileWatcherService` 接口，添加 `StartCompensationScan`、`StopCompensationScan` 方法和 `FilesMissing`、`FilesRestored` 事件
    - _Requirements: 5.1, 5.6_
  - [x] 8.2 在 `FileWatcherService` 中实现补偿扫描逻辑：使用 Timer 定期扫描，通过 IDbContextFactory 获取 DbContext，对比数据库记录与文件系统，触发 FilesMissing/FilesRestored 事件
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_
  - [x] 8.3 在 `VideoManagerOptions` 中添加 `CompensationScanIntervalHours` 配置项，在 MainViewModel 初始化时启动补偿扫描
    - _Requirements: 5.1_
  - [x] 8.4 编写属性测试验证补偿扫描文件对比正确性
    - **Property 8: 补偿扫描文件对比正确性**
    - **Validates: Requirements 5.2, 5.3, 5.4**
  - [x] 8.5 编写单元测试覆盖空数据库和扫描异常情况
    - _Requirements: 5.5_

- [x] 9. 检查点 - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 10. MainViewModel 职责拆分
  - [x] 10.1 定义 Messenger 消息类型：创建 `VideoManager/ViewModels/Messages.cs`，包含 PageChangedMessage、SortChangedMessage、BatchOperationCompletedMessage、RefreshRequestedMessage
    - _Requirements: 7.4, 7.5_
  - [x] 10.2 创建 `VideoManager/ViewModels/PaginationViewModel.cs`，从 MainViewModel 提取分页逻辑（CurrentPage、TotalPages、PageInfoText、PreviousPage、NextPage），通过 WeakReferenceMessenger 发送 PageChangedMessage
    - _Requirements: 7.1, 7.4_
  - [x] 10.3 创建 `VideoManager/ViewModels/SortViewModel.cs`，从 MainViewModel 提取排序逻辑（CurrentSortField、CurrentSortDirection、ToggleSortDirection），通过 WeakReferenceMessenger 发送 SortChangedMessage
    - _Requirements: 7.2, 7.5_
  - [x] 10.4 创建 `VideoManager/ViewModels/BatchOperationViewModel.cs`，从 MainViewModel 提取批量操作逻辑（BatchDelete、BatchTag、BatchCategory），集成 BatchChunkProcessor 进行分块处理
    - _Requirements: 7.3, 2.1, 2.2, 2.3_
  - [x] 10.5 重构 MainViewModel，移除已拆分的逻辑，注入新的子 ViewModel，通过 Messenger 接收消息并协调响应
    - _Requirements: 7.6_
  - [x] 10.6 更新 `App.xaml.cs` 的 DI 注册，添加 PaginationViewModel、SortViewModel、BatchOperationViewModel
    - _Requirements: 7.1, 7.2, 7.3_
  - [x] 10.7 更新 XAML 绑定，将 MainWindow/VideoListView 中的分页、排序、批量操作绑定指向新的子 ViewModel
    - _Requirements: 7.1, 7.2, 7.3_
  - [x] 10.8 编写单元测试验证各子 ViewModel 的 Messenger 消息发送和 MainViewModel 的消息接收
    - _Requirements: 7.4, 7.5, 7.7_

- [x] 11. GlobalExceptionHandler 全局异常处理
  - [x] 11.1 创建 `VideoManager/Services/GlobalExceptionHandler.cs`，实现 Register 方法注册 DispatcherUnhandledException 和 UnobservedTaskException 处理器，通过 ILogger 记录异常，通过 IDialogService 展示友好提示
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_
  - [x] 11.2 重构 `App.xaml.cs`，将现有的内联异常处理替换为 GlobalExceptionHandler，在 DI 容器构建后初始化
    - _Requirements: 6.6_
  - [x] 11.3 编写单元测试验证 GlobalExceptionHandler 的异常处理行为（日志记录、Handled 标志）
    - _Requirements: 6.3, 6.5_

- [x] 12. IAsyncDisposable 实现
  - [x] 12.1 为 MetricsService 添加 `IAsyncDisposable` 实现，在 `DisposeAsync` 中释放 Timer，保留现有 `IDisposable`
    - _Requirements: 9.1, 9.4_
  - [x] 12.2 为 FileWatcherService 添加 `IAsyncDisposable` 实现，在 `DisposeAsync` 中停止补偿扫描和文件监控，保留现有 `IDisposable`
    - _Requirements: 9.2, 9.4_
  - [x] 12.3 更新 `App.xaml.cs` 的 `OnExit`，调用所有 `IAsyncDisposable` 服务的 `DisposeAsync`
    - _Requirements: 9.3_
  - [x] 12.4 编写单元测试验证 DisposeAsync 正确释放资源
    - _Requirements: 9.1, 9.2_

- [x] 13. 最终检查点 - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

## 备注

- 标记 `*` 的任务为可选测试任务，可跳过以加速 MVP
- 每个任务引用具体需求以确保可追溯性
- 检查点确保增量验证
- 属性测试验证通用正确性属性
- 单元测试验证具体示例和边界情况
