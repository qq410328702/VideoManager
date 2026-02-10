# 需求文档

## 简介

VideoManager 桌面应用（WPF / .NET 8）第三轮优化，聚焦六大领域：内存管理、数据库性能、UI 响应性、数据安全、架构解耦和可观测性。在前两轮优化（v1: 上下文菜单/搜索防抖/排序/快捷键/批量操作/缩略图懒加载/数据库索引/并行元数据提取/MainViewModel 提取/IOptions/文件监控；v2: AsNoTracking/结构化日志/Data Annotations 验证/Polly 重试/批量数据库操作/软删除）的基础上，进一步提升应用的性能、可靠性和可维护性。

## 术语表

- **VideoManager**：本 WPF 桌面视频管理应用
- **ThumbnailCacheService**：缩略图缓存服务，负责在内存中缓存缩略图路径
- **LRU_Cache**：最近最少使用缓存，当容量达到上限时淘汰最久未访问的条目
- **WeakReference**：.NET 弱引用，允许 GC 在内存压力时回收被引用对象
- **Compiled_Query**：EF Core 编译查询，预编译 LINQ 表达式以减少重复编译开销
- **CancellationToken**：.NET 取消令牌，用于协作式取消长时间运行的异步操作
- **NavigationService**：导航服务，负责解耦视图导航逻辑与代码隐藏
- **DialogService**：对话框服务，负责通过 ViewModel 层统一管理所有对话框交互
- **BackupService**：数据库备份服务，负责 SQLite 数据库的自动备份与恢复
- **MetricsService**：性能指标服务，负责收集和报告应用运行时性能数据
- **DiagnosticsView**：诊断视图，向用户展示应用性能统计和内存使用信息

## 需求

### 需求 1：LRU 缩略图缓存

**用户故事：** 作为用户，我希望应用在管理大量视频时内存占用保持可控，以便长时间使用不会导致内存溢出。

#### 验收标准

1. THE ThumbnailCacheService SHALL 使用 LRU 淘汰策略替代当前无限增长的 ConcurrentDictionary 缓存
2. WHEN LRU_Cache 中的条目数量达到可配置的最大容量时，THE ThumbnailCacheService SHALL 淘汰最近最少使用的条目
3. THE VideoManagerOptions SHALL 包含一个 ThumbnailCacheMaxSize 配置项，默认值为 1000
4. WHEN 访问一个已缓存的条目时，THE LRU_Cache SHALL 将该条目标记为最近使用
5. WHEN 缓存未命中时，THE ThumbnailCacheService SHALL 将新条目插入缓存，并在超出容量时淘汰最旧条目

### 需求 2：WeakReference 缩略图优化

**用户故事：** 作为用户，我希望不在屏幕上显示的缩略图能被系统自动回收，以便在内存压力较大时释放资源。

#### 验收标准

1. THE ThumbnailCacheService SHALL 对非当前可见的缩略图使用 WeakReference 进行包装
2. WHEN GC 回收了一个 WeakReference 引用的缩略图时，THE ThumbnailCacheService SHALL 在下次访问时重新加载该缩略图
3. WHEN 缩略图被 UI 控件主动引用时，THE ThumbnailCacheService SHALL 保持强引用以防止 GC 回收

### 需求 3：内存使用监控

**用户故事：** 作为用户，我希望能了解应用当前的内存使用情况，以便在出现问题时进行排查。

#### 验收标准

1. THE MetricsService SHALL 定期采集应用的托管堆内存使用量和缩略图缓存条目数
2. WHEN 内存使用量超过可配置的阈值时，THE MetricsService SHALL 记录一条警告级别的日志
3. THE MetricsService SHALL 以不超过 5 秒的间隔采集内存指标

### 需求 4：EF Core 编译查询

**用户故事：** 作为用户，我希望搜索和筛选操作更快响应，以便在大量视频中快速找到目标。

#### 验收标准

1. THE SearchService SHALL 对关键字搜索、标签筛选、日期范围筛选和时长范围筛选使用 EF Core Compiled_Query
2. WHEN 执行编译查询时，THE SearchService SHALL 跳过 LINQ 表达式的重复编译步骤
3. THE VideoRepository SHALL 对分页查询使用 Compiled_Query 以减少查询编译开销

### 需求 5：SQLite 连接池优化

**用户故事：** 作为用户，我希望数据库操作在并发场景下保持稳定高效，以便批量操作不会出现连接争用。

#### 验收标准

1. THE VideoManagerDbContext SHALL 配置 SQLite 连接池参数，包括最大连接数和连接空闲超时
2. WHEN 配置连接池时，THE VideoManagerDbContext SHALL 启用 WAL（Write-Ahead Logging）模式以提升读写并发性能
3. THE VideoManagerOptions SHALL 包含 SQLite 连接池相关的配置项

### 需求 6：长时间操作取消支持

**用户故事：** 作为用户，我希望能取消正在进行的导入、搜索或批量操作，以便在误操作或需要中断时不必等待操作完成。

#### 验收标准

1. WHEN 用户点击取消按钮时，THE ImportService SHALL 通过 CancellationToken 协作式取消当前导入操作
2. WHEN 用户点击取消按钮时，THE SearchService SHALL 通过 CancellationToken 取消当前搜索操作
3. WHEN 用户点击取消按钮时，THE DeleteService SHALL 通过 CancellationToken 取消当前批量删除操作
4. WHEN 操作被取消时，THE VideoManager SHALL 保持数据一致性，已完成的部分操作结果保留
5. THE ImportViewModel SHALL 在导入界面提供一个取消按钮，绑定到 CancellationTokenSource

### 需求 7：批量操作进度与预估时间

**用户故事：** 作为用户，我希望在批量操作时看到进度百分比和预估剩余时间，以便了解操作何时完成。

#### 验收标准

1. WHEN 批量导入正在进行时，THE ImportViewModel SHALL 显示已完成数量、总数量、进度百分比和预估剩余时间
2. WHEN 批量删除正在进行时，THE VideoListViewModel SHALL 显示已完成数量、总数量和预估剩余时间
3. THE VideoManager SHALL 基于已完成操作的平均耗时计算预估剩余时间

### 需求 8：自动数据库备份

**用户故事：** 作为用户，我希望数据库能自动备份，以便在数据损坏或误操作时能恢复数据。

#### 验收标准

1. THE BackupService SHALL 在应用启动时自动执行一次数据库备份
2. THE BackupService SHALL 按可配置的时间间隔（默认每 24 小时）执行定期备份
3. WHEN 执行批量删除操作前，THE BackupService SHALL 自动创建一个备份
4. THE BackupService SHALL 保留最近 N 个备份文件（N 可配置，默认为 5），自动清理更早的备份
5. THE BackupService SHALL 将备份文件存储在可配置的目录中，默认为应用数据目录下的 Backups 子目录

### 需求 9：数据库完整性检查

**用户故事：** 作为用户，我希望应用在启动时检查数据库完整性，以便在数据损坏时及早发现并处理。

#### 验收标准

1. WHEN 应用启动时，THE BackupService SHALL 执行 SQLite 的 PRAGMA integrity_check 命令
2. IF 完整性检查发现数据库损坏，THEN THE BackupService SHALL 记录错误日志并尝试从最近的备份恢复
3. IF 没有可用的备份文件，THEN THE BackupService SHALL 记录严重错误日志并通知用户

### 需求 10：备份恢复功能

**用户故事：** 作为用户，我希望能从备份中恢复数据库，以便在数据出现问题时手动恢复到之前的状态。

#### 验收标准

1. THE BackupService SHALL 提供列出所有可用备份文件及其创建时间的功能
2. WHEN 用户选择一个备份文件进行恢复时，THE BackupService SHALL 先备份当前数据库，再用选定的备份替换当前数据库
3. WHEN 恢复完成后，THE VideoManager SHALL 重新加载数据库连接并刷新所有视图数据

### 需求 11：导航服务解耦

**用户故事：** 作为开发者，我希望视图导航逻辑从 MainWindow 代码隐藏中提取出来，以便通过 ViewModel 层控制导航，提升可测试性。

#### 验收标准

1. THE NavigationService SHALL 提供打开视频播放器窗口的方法，接受 VideoEntry 参数
2. THE NavigationService SHALL 提供打开导入对话框的方法，返回导入结果
3. WHEN MainWindow 代码隐藏中的导航逻辑迁移到 NavigationService 后，THE MainWindow 代码隐藏 SHALL 不再直接创建或管理子窗口

### 需求 12：对话框服务解耦

**用户故事：** 作为开发者，我希望所有对话框交互通过统一的服务管理，以便 ViewModel 层不依赖具体的 View 实现，提升可测试性。

#### 验收标准

1. THE DialogService SHALL 提供显示编辑对话框的方法，接受 VideoEntry 参数并返回编辑结果
2. THE DialogService SHALL 提供显示删除确认对话框的方法，返回用户的删除选择
3. THE DialogService SHALL 提供显示批量标签对话框的方法，返回用户选择的标签列表
4. THE DialogService SHALL 提供显示批量分类对话框的方法，返回用户选择的分类
5. THE DialogService SHALL 提供显示消息提示框的方法，替代 MainWindow 中直接调用 MessageBox
6. WHEN DialogService 实现完成后，THE MainWindow 代码隐藏 SHALL 仅保留窗口生命周期管理和 DataContext 绑定逻辑

### 需求 13：性能指标收集

**用户故事：** 作为用户，我希望了解应用各项操作的性能表现，以便在操作缓慢时有据可查。

#### 验收标准

1. THE MetricsService SHALL 记录每次导入操作的总耗时和每个文件的平均处理时间
2. THE MetricsService SHALL 记录每次搜索操作的响应时间
3. THE MetricsService SHALL 记录每次缩略图生成操作的耗时
4. THE MetricsService SHALL 将性能指标以结构化日志的形式输出

### 需求 14：诊断统计视图

**用户故事：** 作为用户，我希望在应用内查看性能统计和内存使用信息，以便直观了解应用运行状态。

#### 验收标准

1. THE DiagnosticsView SHALL 显示当前内存使用量、缩略图缓存命中率和缓存条目数
2. THE DiagnosticsView SHALL 显示最近 N 次导入操作的平均耗时和最近 N 次搜索的平均响应时间
3. THE DiagnosticsView SHALL 显示数据库文件大小和最近一次备份的时间
4. WHEN 用户打开诊断视图时，THE DiagnosticsView SHALL 实时刷新显示的指标数据
