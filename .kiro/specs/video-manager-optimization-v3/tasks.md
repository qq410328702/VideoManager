# 实施计划：VideoManager 第三轮优化

## 概述

基于设计文档，按增量方式实现六大优化领域。每个任务构建在前一个任务之上，测试任务紧跟实现任务以尽早发现问题。

## 任务

- [x] 1. 实现 LRU 缓存与缩略图缓存重构
  - [x] 1.1 创建 LruCache<TKey, TValue> 泛型类
    - 在 `VideoManager/Services/` 下创建 `LruCache.cs`
    - 基于 `LinkedList` + `Dictionary` 实现，使用 `lock` 保证线程安全
    - 包含 `TryGet`、`Put`、`Remove`、`Clear`、`Count`、`Capacity` 成员
    - _Requirements: 1.1, 1.2, 1.4, 1.5_

  - [x] 1.2 编写 LruCache 属性测试
    - **Property 1: LRU 缓存不变量**
    - 使用 FsCheck 生成随机 Put/TryGet 操作序列，验证容量不变量和淘汰顺序
    - **Validates: Requirements 1.1, 1.2, 1.4, 1.5**

  - [x] 1.3 重构 ThumbnailCacheService 使用 LruCache
    - 将 `ConcurrentDictionary<string, string?>` 替换为 `LruCache<string, string?>`
    - 从 `VideoManagerOptions.ThumbnailCacheMaxSize` 读取容量配置
    - 添加 `CacheCount`、`CacheHitCount`、`CacheMissCount` 属性到接口和实现
    - _Requirements: 1.1, 1.2, 1.3, 1.5_

  - [x] 1.4 扩展 VideoManagerOptions 添加缓存配置
    - 添加 `ThumbnailCacheMaxSize` 属性，默认值 1000
    - 在 `App.xaml.cs` 的 `ConfigureServices` 中注入配置
    - _Requirements: 1.3_

  - [x] 1.5 编写 ThumbnailCacheService 单元测试
    - 测试缓存命中/未命中行为、容量限制边界、清除缓存
    - _Requirements: 1.1, 1.2, 1.5_

- [x] 2. 实现 WeakReference 缩略图优化
  - [x] 2.1 在 ThumbnailCacheService 中集成 WeakReference
    - 将 LruCache 的 value 类型改为 `WeakReference<string>`
    - 当 `WeakReference.TryGetTarget` 失败时重新加载
    - 保持对当前可见缩略图的强引用逻辑
    - _Requirements: 2.1, 2.2, 2.3_

  - [x] 2.2 编写 WeakReference 缓存单元测试
    - 测试 WeakReference 目标被回收后的重新加载行为
    - 测试强引用保持逻辑
    - _Requirements: 2.1, 2.2, 2.3_

- [x] 3. 检查点 - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 4. 实现 MetricsService 与内存监控
  - [x] 4.1 创建 IMetricsService 接口和 MetricsService 实现
    - 在 `VideoManager/Services/` 下创建 `IMetricsService.cs` 和 `MetricsService.cs`
    - 实现 `StartTimer` 返回 IDisposable 计时器
    - 实现 `GetAverageTime`、`GetLastTime` 基于 `ConcurrentDictionary<string, List<TimeSpan>>`
    - 使用 `System.Threading.Timer` 每 5 秒采集 `GC.GetTotalMemory` 和缓存指标
    - 超阈值时通过 ILogger 记录警告日志
    - _Requirements: 3.1, 3.2, 3.3, 13.1, 13.2, 13.3, 13.4_

  - [x] 4.2 编写 MetricsService 属性测试
    - **Property 8: 指标计时器准确性**
    - 使用 FsCheck 生成随机延迟序列，验证记录耗时和平均值计算
    - **Validates: Requirements 13.1, 13.2, 13.3**

  - [x] 4.3 扩展 VideoManagerOptions 添加内存监控配置
    - 添加 `MemoryWarningThresholdMb` 属性，默认值 512
    - 在 DI 容器中注册 MetricsService 为 Singleton
    - _Requirements: 3.2_

  - [x] 4.4 编写 MetricsService 单元测试
    - 测试内存阈值警告日志触发
    - 测试空操作列表的平均值计算
    - _Requirements: 3.1, 3.2, 13.4_

- [x] 5. 实现 EF Core 编译查询与 SQLite WAL 模式
  - [x] 5.1 创建 CompiledQueries 静态类
    - 在 `VideoManager/Data/` 下创建 `CompiledQueries.cs`
    - 定义关键字搜索和默认分页的编译查询
    - _Requirements: 4.1, 4.3_

  - [x] 5.2 重构 SearchService 使用编译查询
    - 对纯关键字搜索路径使用编译查询
    - 多条件组合查询保持动态 LINQ，编译查询失败时回退到动态查询
    - _Requirements: 4.1, 4.2_

  - [x] 5.3 重构 VideoRepository 使用编译查询
    - 对默认排序的分页查询使用编译查询
    - _Requirements: 4.3_

  - [x] 5.4 编写编译查询属性测试
    - **Property 2: 编译查询等价性**
    - 使用 FsCheck 生成随机 SearchCriteria，对比编译查询与动态查询结果
    - **Validates: Requirements 4.1, 4.3**

  - [x] 5.5 配置 SQLite WAL 模式
    - 在 `App.xaml.cs` 启动时执行 `PRAGMA journal_mode=WAL`、`PRAGMA synchronous=NORMAL`、`PRAGMA cache_size=-8000`
    - 配置连接字符串添加 `Cache=Shared`
    - _Requirements: 5.1, 5.2, 5.3_

  - [x] 5.6 编写 WAL 模式单元测试
    - 验证启动后 `PRAGMA journal_mode` 返回 "wal"
    - _Requirements: 5.2_

- [x] 6. 检查点 - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 7. 实现取消操作支持与进度预估
  - [x] 7.1 创建 ProgressEstimator 类
    - 在 `VideoManager/Services/` 下创建 `ProgressEstimator.cs`
    - 实现基于移动平均（最近 10 项）的预估剩余时间计算
    - 包含 `RecordCompletion`、`EstimatedTimeRemaining`、`ProgressPercentage`、`CompletedCount`、`TotalCount`
    - _Requirements: 7.1, 7.2, 7.3_

  - [x] 7.2 编写 ProgressEstimator 属性测试
    - **Property 4: 进度预估准确性**
    - 使用 FsCheck 生成随机完成事件序列，验证百分比和预估时间计算
    - **Validates: Requirements 7.1, 7.2, 7.3**

  - [x] 7.3 增强 ImportViewModel 的取消与进度显示
    - 在进度报告中集成 ProgressEstimator，显示预估剩余时间
    - 确保取消按钮正确触发 CancellationTokenSource.Cancel
    - _Requirements: 6.1, 6.5, 7.1_

  - [x] 7.4 增强 MainWindow 批量操作的取消与进度显示
    - 在批量删除、批量标签、批量分类操作中传递 CancellationToken（替代 CancellationToken.None）
    - 添加取消按钮到批量操作进度 UI
    - 集成 ProgressEstimator 显示预估剩余时间
    - _Requirements: 6.3, 6.4, 7.2_

  - [x] 7.5 编写取消操作属性测试
    - **Property 3: 取消操作保持数据一致性**
    - 使用 FsCheck 生成随机批量大小和取消点，验证已完成项目的持久化
    - **Validates: Requirements 6.4**

  - [x] 7.6 编写取消操作单元测试
    - 测试导入取消后 UI 状态更新为"已取消"
    - 测试搜索取消后不返回部分结果
    - _Requirements: 6.1, 6.2, 6.3_

- [x] 8. 检查点 - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 9. 实现数据库备份与恢复
  - [x] 9.1 创建 IBackupService 接口和 BackupService 实现
    - 在 `VideoManager/Services/` 下创建 `IBackupService.cs` 和 `BackupService.cs`
    - 实现 `CreateBackupAsync`（使用 `VACUUM INTO`）
    - 实现 `CheckIntegrityAsync`（使用 `PRAGMA integrity_check`）
    - 实现 `RestoreFromBackupAsync`（先备份当前 DB → 复制备份覆盖）
    - 实现 `ListBackups` 和 `CleanupOldBackupsAsync`
    - 备份文件命名：`videomanager_backup_{yyyyMMdd_HHmmss}.db`
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 9.1, 9.2, 9.3, 10.1, 10.2_

  - [x] 9.2 扩展 VideoManagerOptions 添加备份配置
    - 添加 `BackupDirectory`、`MaxBackupCount`（默认 5）、`BackupIntervalHours`（默认 24）
    - 在 `App.xaml.cs` 中配置默认备份目录为 `AppDir/Backups`
    - _Requirements: 8.4, 8.5_

  - [x] 9.3 集成备份到应用生命周期
    - 在 `App.xaml.cs` 启动时调用 `CheckIntegrityAsync` 和 `CreateBackupAsync`
    - 启动定期备份 Timer
    - 在批量删除前调用 `CreateBackupAsync`
    - 在 DI 容器中注册 BackupService 为 Singleton
    - _Requirements: 8.1, 8.2, 8.3, 9.1, 9.2, 9.3_

  - [x] 9.4 编写备份保留属性测试
    - **Property 5: 备份保留数量不变量**
    - 使用 FsCheck 生成随机数量的备份文件，验证清理后保留数量
    - **Validates: Requirements 8.4**

  - [x] 9.5 编写备份列表属性测试
    - **Property 6: 备份列表完整性**
    - 使用 FsCheck 在临时目录创建随机备份文件，验证 ListBackups 返回完整列表
    - **Validates: Requirements 10.1**

  - [x] 9.6 编写备份恢复属性测试
    - **Property 7: 备份恢复往返一致性**
    - 创建随机数据库状态，备份、修改、恢复，验证数据一致
    - **Validates: Requirements 10.2**

  - [x] 9.7 编写备份单元测试
    - 测试空备份目录的 ListBackups
    - 测试损坏数据库的完整性检查
    - 测试无备份时的恢复尝试
    - _Requirements: 9.1, 9.2, 9.3_

- [x] 10. 检查点 - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 11. 实现导航服务与对话框服务解耦
  - [x] 11.1 创建 INavigationService 接口和 NavigationService 实现
    - 在 `VideoManager/Services/` 下创建 `INavigationService.cs` 和 `NavigationService.cs`
    - 实现 `OpenVideoPlayerAsync` 和 `OpenImportDialogAsync`
    - 通过 `Application.Current.Dispatcher` 确保 UI 线程操作
    - _Requirements: 11.1, 11.2_

  - [x] 11.2 创建 IDialogService 接口和 DialogService 实现
    - 在 `VideoManager/Services/` 下创建 `IDialogService.cs` 和 `DialogService.cs`
    - 实现编辑、删除确认、批量标签、批量分类、消息提示等对话框方法
    - 定义 `MessageLevel` 枚举
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

  - [x] 11.3 重构 MainWindow 代码隐藏
    - 将 `VideoListControl_VideoDoubleClicked` 逻辑迁移到 MainViewModel + NavigationService
    - 将 `ImportButton_Click` 逻辑迁移到 MainViewModel + NavigationService
    - 将所有对话框创建逻辑迁移到 MainViewModel + DialogService
    - 将所有 `MessageBox.Show` 调用替换为 DialogService 调用
    - MainWindow 代码隐藏仅保留窗口生命周期管理、DataContext 绑定和键盘快捷键处理
    - _Requirements: 11.3, 12.6_

  - [x] 11.4 在 DI 容器中注册新服务
    - 在 `App.xaml.cs` 中注册 NavigationService 和 DialogService
    - 更新 MainViewModel 构造函数注入新服务
    - _Requirements: 11.1, 11.2, 12.1_

  - [x] 11.5 编写 NavigationService 和 DialogService 单元测试
    - 使用 mock 验证接口契约
    - 测试 MainViewModel 通过服务调用对话框的流程
    - _Requirements: 11.1, 11.2, 12.1, 12.2, 12.3, 12.4, 12.5_

- [x] 12. 实现诊断统计视图
  - [x] 12.1 创建 DiagnosticsViewModel
    - 在 `VideoManager/ViewModels/` 下创建 `DiagnosticsViewModel.cs`
    - 注入 IMetricsService 和 IBackupService
    - 实现 RefreshAsync 和 RestoreBackupAsync 命令
    - 使用 DispatcherTimer 每 5 秒自动刷新
    - _Requirements: 14.1, 14.2, 14.3, 14.4_

  - [x] 12.2 创建 DiagnosticsView
    - 在 `VideoManager/Views/` 下创建 `DiagnosticsView.xaml` 和 `DiagnosticsView.xaml.cs`
    - 使用 Material Design 卡片布局展示内存、缓存、性能、备份信息
    - _Requirements: 14.1, 14.2, 14.3_

  - [x] 12.3 集成诊断视图到主界面
    - 在 MainWindow 中添加打开诊断视图的入口（菜单或按钮）
    - 通过 NavigationService 或 DialogService 打开诊断窗口
    - 在 DI 容器中注册 DiagnosticsViewModel
    - _Requirements: 14.4_

- [x] 13. 集成 MetricsService 到现有服务
  - [x] 13.1 在 ImportService 中集成计时
    - 在 `ImportVideosAsync` 中使用 `MetricsService.StartTimer("import")` 记录总耗时
    - 在每个文件处理中记录单文件耗时
    - _Requirements: 13.1_

  - [x] 13.2 在 SearchService 中集成计时
    - 在 `SearchAsync` 中使用 `MetricsService.StartTimer("search")` 记录搜索耗时
    - _Requirements: 13.2_

  - [x] 13.3 在 FFmpegService 中集成缩略图生成计时
    - 在缩略图生成方法中使用 `MetricsService.StartTimer("thumbnail_generation")` 记录耗时
    - _Requirements: 13.3_

- [x] 14. 最终检查点 - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

## 备注

- 标记 `*` 的任务为可选任务，可跳过以加速 MVP 交付
- 每个任务引用了具体的需求编号以确保可追溯性
- 检查点确保增量验证
- 属性测试验证通用正确性属性，单元测试验证具体示例和边界情况
- FsCheck 需要添加到 `VideoManager.Tests.csproj` 的依赖中
