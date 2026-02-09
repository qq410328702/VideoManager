# Implementation Plan: 视频管理器优化

## Overview

基于现有 WPF 视频管理应用进行增量优化。按照依赖关系分阶段实施：先完成架构基础（配置模式、MainViewModel），再实现核心功能（删除、排序、搜索防抖），然后添加增强功能（批量操作、播放器快捷键、缩略图懒加载、文件监控），最后完成 UI 集成。

## Tasks

- [x] 1. 架构基础：IOptions 配置模式与 MainViewModel
  - [x] 1.1 创建 VideoManagerOptions 配置类和 DI 注册
    - 在 `VideoManager/Models/` 下创建 `VideoManagerOptions.cs`，包含 `VideoLibraryPath` 和 `ThumbnailDirectory` 属性
    - 修改 `App.xaml.cs` 中的 `ConfigureServices`，使用 `IOptions<VideoManagerOptions>` 注册配置
    - 重构 `ImportService` 构造函数，接收 `IOptions<VideoManagerOptions>` 替代字符串参数
    - _Requirements: 11.1, 11.2, 11.3_

  - [x] 1.2 创建 MainViewModel 并迁移协调逻辑
    - 在 `VideoManager/ViewModels/` 下创建 `MainViewModel.cs`
    - 将 `MainWindow.xaml.cs` 中的搜索触发、分页控制、刷新、状态文本更新逻辑迁移到 MainViewModel
    - MainViewModel 通过 DI 接收 VideoListViewModel、SearchViewModel、CategoryViewModel
    - 修改 `MainWindow.xaml.cs` 仅保留对话框窗口创建等纯 UI 操作
    - 更新 `MainWindow.xaml` 使用数据绑定和命令替代事件处理
    - 在 `App.xaml.cs` 中注册 MainViewModel
    - _Requirements: 10.1, 10.2, 10.3_

  - [x] 1.3 为 MainViewModel 编写单元测试
    - 测试搜索触发、分页控制、状态文本更新逻辑
    - _Requirements: 10.1_

- [x] 2. Checkpoint - 确保架构基础重构后所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 3. 数据模型扩展与数据库优化
  - [x] 3.1 扩展 Tag 模型添加 Color 属性
    - 修改 `VideoManager/Models/Tag.cs`，添加 `string? Color` 属性
    - 修改 `VideoManagerDbContext.OnModelCreating`，配置 `Tag.Color` 列（MaxLength=9）
    - 添加 EF Core 迁移
    - _Requirements: 14.1_

  - [x] 3.2 添加数据库索引优化
    - 在 `VideoManagerDbContext.OnModelCreating` 中为 `VideoEntry.FileSize` 添加索引
    - 验证 Title、DurationTicks、ImportedAt 索引已存在
    - 添加 EF Core 迁移
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

  - [x] 3.3 扩展 SearchCriteria 支持排序
    - 创建 `SortField` 和 `SortDirection` 枚举
    - 修改 `SearchCriteria` record 添加 `SortBy` 和 `SortDir` 参数
    - 修改 `SearchService.SearchAsync` 实现排序逻辑
    - 修改 `VideoRepository.GetPagedAsync` 支持排序参数
    - _Requirements: 3.2, 3.3, 8.5_

  - [x] 3.4 编写排序正确性属性测试
    - **Property 2: 排序正确性**
    - **Validates: Requirements 3.2, 3.3**

- [x] 4. 视频删除功能
  - [x] 4.1 创建 DeleteService
    - 在 `VideoManager/Services/` 下创建 `IDeleteService.cs` 和 `DeleteService.cs`
    - 实现 `DeleteVideoAsync`：根据 deleteFile 参数决定是否删除源文件和缩略图
    - 实现 `BatchDeleteAsync`：批量删除，支持进度报告
    - 在 `App.xaml.cs` 中注册 DeleteService
    - _Requirements: 12.1, 12.2, 12.4_

  - [x] 4.2 编写删除功能属性测试
    - **Property 11: 仅从库中删除**
    - **Property 12: 删除并移除源文件**
    - **Validates: Requirements 12.1, 12.2**

- [x] 5. 搜索防抖与实时搜索
  - [x] 5.1 在 MainViewModel 中实现防抖搜索
    - 添加 `SearchKeyword` 属性，使用 `partial void OnSearchKeywordChanged` 触发防抖
    - 使用 `CancellationTokenSource` 实现 300ms 防抖逻辑
    - 新输入到达时取消正在执行的搜索
    - 搜索框清空时恢复完整视频列表
    - 更新 `MainWindow.xaml` 将搜索框绑定到 MainViewModel.SearchKeyword
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 5.2 编写防抖搜索属性测试
    - **Property 1: 防抖搜索行为**
    - **Validates: Requirements 2.1, 2.2, 2.3**

- [x] 6. Checkpoint - 确保核心功能测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 7. 播放器增强
  - [x] 7.1 扩展 VideoPlayerViewModel 添加跳转和速度控制
    - 添加 `PlaybackSpeed` 属性和 `SpeedOptions` 数组（0.5, 1.0, 1.5, 2.0）
    - 实现 `CycleSpeed()` 命令，循环切换速度
    - 实现 `Skip(double seconds)` 方法，带边界限制
    - 实现 `TogglePlayPause()` 命令
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x] 7.2 在 VideoPlayerView 中添加键盘快捷键处理
    - 在 `VideoPlayerView.xaml.cs` 中处理 KeyDown 事件
    - 左方向键调用 `Skip(-5)`，右方向键调用 `Skip(5)`
    - 空格键调用 `TogglePlayPause()`
    - 速度控制快捷键（如 S 键）调用 `CycleSpeed()`
    - 绑定 PlaybackSpeed 到 MediaElement.SpeedRatio
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [x] 7.3 编写播放器属性测试
    - **Property 3: 播放位置跳转与边界限制**
    - **Property 4: 播放/暂停状态切换**
    - **Property 5: 播放速度循环切换**
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5**

- [x] 8. 窗口设置记忆
  - [x] 8.1 创建 WindowSettingsService
    - 在 `VideoManager/Services/` 下创建 `IWindowSettingsService.cs` 和 `WindowSettingsService.cs`
    - 实现 JSON 文件读写（路径：`{AppBaseDirectory}/Data/window-settings.json`）
    - 创建 `WindowSettings` record（Left, Top, Width, Height, IsMaximized）
    - 加载时验证窗口位置是否在屏幕范围内，超出则重置居中
    - 在 `App.xaml.cs` 中注册 WindowSettingsService
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [x] 8.2 集成窗口设置到 MainWindow
    - 在 MainWindow 构造函数中加载窗口设置并应用
    - 在 MainWindow Closing 事件中保存窗口设置
    - _Requirements: 5.1, 5.2_

  - [x] 8.3 编写窗口设置属性测试
    - **Property 6: 窗口设置 round-trip**
    - **Validates: Requirements 5.1, 5.2**

- [x] 9. 批量操作
  - [x] 9.1 扩展 EditService 支持批量操作
    - 在 `IEditService` 中添加 `BatchAddTagAsync` 和 `BatchMoveToCategoryAsync` 方法
    - 在 `EditService` 中实现批量标签分配和批量分类移动
    - _Requirements: 6.3, 6.4_

  - [x] 9.2 实现批量操作 UI
    - 修改 `VideoListView.xaml` 的 ListBox 为 `SelectionMode="Extended"` 支持多选
    - 在 VideoListViewModel 中添加 `SelectedVideos` 集合属性
    - 创建批量操作工具栏（多选时显示），包含批量删除、批量标签、批量分类按钮
    - 创建批量标签选择对话框和批量分类选择对话框
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

  - [x] 9.3 编写批量操作属性测试
    - **Property 7: 批量标签分配**
    - **Property 8: 批量分类移动**
    - **Validates: Requirements 6.3, 6.4**

- [x] 10. Checkpoint - 确保增强功能测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 11. 缩略图懒加载集成
  - [x] 11.1 创建 ThumbnailCacheService
    - 在 `VideoManager/Services/` 下创建 `IThumbnailCacheService.cs` 和 `ThumbnailCacheService.cs`
    - 使用 `ConcurrentDictionary<string, string?>` 实现内存缓存
    - `LoadThumbnailAsync`：先查缓存，未命中则验证文件存在后返回路径
    - 在 `App.xaml.cs` 中注册为 Singleton
    - _Requirements: 7.2, 7.3_

  - [x] 11.2 将 ThumbnailCacheService 注入 VideoListViewModel
    - 修改 `App.xaml.cs` 中 VideoListViewModel 的注册，将 `ThumbnailCacheService.LoadThumbnailAsync` 作为 `thumbnailLoader` 参数传入
    - _Requirements: 7.1, 7.4_

  - [x] 11.3 编写缩略图缓存属性测试
    - **Property 9: 缩略图缓存幂等性**
    - **Validates: Requirements 7.2**

- [x] 12. 并行导入优化
  - [x] 12.1 重构 ImportService 支持并行元数据提取
    - 修改 `ImportService.ImportVideosAsync`，文件复制保持串行，元数据提取和缩略图生成使用 `SemaphoreSlim` 控制并行度（`Environment.ProcessorCount`）
    - 确保进度报告线程安全（使用 `Interlocked` 计数）
    - _Requirements: 9.1, 9.2, 9.3_

  - [x] 12.2 编写并行导入属性测试
    - **Property 10: 并行导入一致性**
    - **Validates: Requirements 9.1, 9.2**

- [x] 13. 文件系统监控
  - [x] 13.1 创建 FileWatcherService
    - 在 `VideoManager/Services/` 下创建 `IFileWatcherService.cs` 和 `FileWatcherService.cs`
    - 使用 `FileSystemWatcher` 监控 Video_Library 目录
    - 实现 `FileDeleted` 和 `FileRenamed` 事件
    - 初始化失败时记录日志并降级
    - 在 `App.xaml.cs` 中注册为 Singleton
    - _Requirements: 15.1, 15.2, 15.3, 15.4_

  - [x] 13.2 集成 FileWatcher 到 MainViewModel
    - MainViewModel 订阅 FileWatcherService 事件
    - 文件删除时标记对应 VideoEntry 的 IsFileMissing
    - 文件重命名时更新对应 VideoEntry 的 FilePath
    - 在 VideoListView 中为缺失文件的视频卡片添加视觉提示
    - _Requirements: 15.2, 15.3_

  - [x] 13.3 编写文件监控属性测试
    - **Property 13: 文件删除检测**
    - **Property 14: 文件重命名检测**
    - **Validates: Requirements 15.2, 15.3**

- [x] 14. UI 集成：右键菜单、排序控件、标签颜色、系统播放器
  - [x] 14.1 扩展视频卡片右键上下文菜单
    - 修改 `VideoListView.xaml` 中的 ContextMenu，添加"编辑信息"、"删除视频"、"复制文件路径"、"使用系统播放器打开"菜单项
    - 在 `VideoListView.xaml.cs` 中实现各菜单项的事件处理
    - "编辑信息"打开 EditDialog
    - "复制文件路径"使用 `Clipboard.SetText`
    - "删除视频"调用 DeleteService（通过 ViewModel）
    - "使用系统播放器打开"使用 `Process.Start` 打开文件
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 13.1, 13.2_

  - [x] 14.2 添加排序控件到视频列表区域
    - 在 `MainWindow.xaml` 视频列表头部添加排序 ComboBox（时长/文件大小/导入日期）和排序方向切换按钮
    - 绑定到 MainViewModel 的 CurrentSortField 和 CurrentSortDirection
    - 排序变化时重置分页并重新加载
    - _Requirements: 3.1, 3.4_

  - [x] 14.3 实现标签颜色显示
    - 修改 `VideoListView.xaml` 和 `EditDialog.xaml` 中的 Tag 显示，使用 Tag.Color 作为背景色
    - 在 EditDialog 中添加颜色选择器（使用 Material Design 调色板）
    - 未设置颜色时使用默认主题色
    - _Requirements: 14.2, 14.3, 14.4_

- [x] 15. Final Checkpoint - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties
- Unit tests validate specific examples and edge cases
- 数据库迁移应在 3.1 和 3.2 完成后统一生成一次迁移
