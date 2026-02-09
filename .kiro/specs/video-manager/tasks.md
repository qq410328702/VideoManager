# 实现计划：视频管理桌面应用

## 概述

基于 MVVM 架构，从数据层开始自底向上构建。先搭建项目结构和数据模型，再实现 Service 层核心逻辑，最后构建 ViewModel 和 View 层。测试贯穿各阶段。

## 任务

- [x] 1. 搭建项目结构和基础设施
  - [x] 1.1 创建 WPF 项目和测试项目
    - 创建 `VideoManager` WPF 应用项目（.NET 8）
    - 创建 `VideoManager.Tests` xUnit 测试项目
    - 安装 NuGet 包：MaterialDesignThemes、Microsoft.EntityFrameworkCore.Sqlite、FsCheck.Xunit、Moq
    - 配置 App.xaml 中的 Material Design 主题资源
    - _Requirements: 8.1_

  - [x] 1.2 创建数据模型和 DbContext
    - 实现 `VideoEntry`、`Tag`、`FolderCategory` 实体类
    - 实现 `VideoManagerDbContext`，配置多对多关系、唯一索引、级联删除
    - 创建初始 EF Core 迁移
    - _Requirements: 8.1, 8.2, 8.3_

  - [x] 1.3 编写数据模型属性测试
    - **Property 6: Tag 唯一性约束**
    - **Validates: Requirements 3.1**

- [x] 2. 实现 Repository 层
  - [x] 2.1 实现 VideoRepository
    - 实现 `IVideoRepository` 接口及其 EF Core 实现
    - 包含分页查询逻辑
    - _Requirements: 7.2, 8.1_

  - [x] 2.2 实现 TagRepository
    - 实现 `ITagRepository` 接口及其 EF Core 实现
    - 包含唯一性检查逻辑
    - _Requirements: 3.1, 3.7_

  - [x] 2.3 实现 CategoryRepository
    - 实现 `ICategoryRepository` 接口及其 EF Core 实现
    - 包含树形结构查询逻辑
    - _Requirements: 3.4, 3.6_

  - [x] 2.4 编写 Repository 层属性测试
    - **Property 7: Tag 关联 round-trip**
    - **Validates: Requirements 3.2, 3.3, 6.3**

  - [x] 2.5 编写分类树属性测试
    - **Property 8: 分类树 round-trip**
    - **Validates: Requirements 3.4**

  - [x] 2.6 编写多分类关联属性测试
    - **Property 9: 多分类关联**
    - **Validates: Requirements 3.5**

  - [x] 2.7 编写删除不影响视频属性测试
    - **Property 10: 删除分类或标签不影响视频**
    - **Validates: Requirements 3.6, 3.7**

  - [x] 2.8 编写分页查询属性测试
    - **Property 14: 分页查询正确性**
    - **Validates: Requirements 7.2**

  - [x] 2.9 编写事务回滚属性测试
    - **Property 15: 数据库异常事务回滚**
    - **Validates: Requirements 8.4**

- [x] 3. 检查点 - 确保数据层测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 4. 实现 FFmpeg 服务
  - [x] 4.1 实现 FFmpegService
    - 实现 `IFFmpegService` 接口
    - 通过 `Process` 调用 FFmpeg CLI 提取元信息（ffprobe JSON 输出）
    - 通过 FFmpeg 生成缩略图（提取指定时间点的帧）
    - 处理超时和进程异常
    - _Requirements: 1.8, 2.1, 2.2_

  - [x] 4.2 编写 FFmpegService 单元测试
    - 测试 ffprobe JSON 输出解析逻辑
    - 测试超时处理和错误场景
    - _Requirements: 1.8, 2.2_

- [x] 5. 实现导入服务
  - [x] 5.1 实现文件扫描逻辑
    - 实现 `IImportService.ScanFolderAsync`
    - 递归扫描文件夹，按扩展名过滤支持的视频格式
    - _Requirements: 1.1_

  - [x] 5.2 编写文件扫描属性测试
    - **Property 1: 文件扫描格式过滤**
    - **Validates: Requirements 1.1**

  - [x] 5.3 实现视频导入逻辑
    - 实现 `IImportService.ImportVideosAsync`
    - 文件复制/移动到 Video_Library
    - 重名文件自动重命名
    - 调用 FFmpegService 提取元信息和生成缩略图
    - 创建 Video_Entry 数据库记录
    - 通过 IProgress 报告进度
    - 失败文件跳过并记录
    - _Requirements: 1.3, 1.6, 1.7, 1.8, 2.1, 2.4_

  - [x] 5.4 编写导入 round-trip 属性测试
    - **Property 2: 导入 round-trip**
    - **Validates: Requirements 1.3, 1.8**

  - [x] 5.5 编写导入失败隔离属性测试
    - **Property 3: 导入失败隔离**
    - **Validates: Requirements 1.6**

  - [x] 5.6 编写重名文件属性测试
    - **Property 4: 重名文件自动重命名**
    - **Validates: Requirements 1.7**

  - [x] 5.7 编写缩略图属性测试
    - **Property 5: 缩略图生成与路径记录**
    - **Validates: Requirements 2.1, 2.4**

- [x] 6. 实现搜索服务
  - [x] 6.1 实现 SearchService
    - 实现 `ISearchService.SearchAsync`
    - 支持关键词模糊匹配（标题 + 描述）
    - 支持 Tag 筛选、日期范围、时长范围
    - 多条件取交集
    - 分页返回结果
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x] 6.2 编写搜索结果正确性属性测试
    - **Property 11: 搜索结果正确性**
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5**

- [x] 7. 实现元信息编辑逻辑
  - [x] 7.1 实现视频编辑服务方法
    - 在 VideoRepository 或独立 Service 中实现标题/描述更新
    - 实现标题非空验证逻辑
    - 实现 Tag 关联的增删操作
    - _Requirements: 6.2, 6.3, 6.4_

  - [x] 7.2 编写编辑持久化属性测试
    - **Property 12: 元信息编辑持久化 round-trip**
    - **Validates: Requirements 6.2**

  - [x] 7.3 编写标题非空验证属性测试
    - **Property 13: 标题非空验证**
    - **Validates: Requirements 6.4**

- [x] 8. 检查点 - 确保 Service 层测试通过
  - 确保所有测试通过，如有问题请询问用户。

- [x] 9. 实现 ViewModel 层
  - [x] 9.1 实现 VideoListViewModel
    - 绑定视频列表数据（ObservableCollection）
    - 实现虚拟化加载和分页逻辑
    - 异步加载缩略图
    - _Requirements: 7.1, 7.2, 7.3_

  - [x] 9.2 实现 ImportViewModel
    - 绑定文件夹选择、扫描结果、导入进度
    - 调用 ImportService 执行导入
    - 在后台线程执行，保持 UI 响应
    - _Requirements: 1.2, 1.4, 1.5_

  - [x] 9.3 实现 SearchViewModel
    - 绑定搜索条件输入和结果列表
    - 调用 SearchService 执行搜索
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6_

  - [x] 9.4 实现 CategoryViewModel
    - 绑定分类树形结构和 Tag 列表
    - 实现分类和 Tag 的增删操作
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [x] 9.5 实现 EditViewModel
    - 绑定视频元信息编辑表单
    - 实现保存和验证逻辑
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

  - [x] 9.6 实现 VideoPlayerViewModel
    - 绑定播放控制命令（播放、暂停、停止、音量、进度）
    - 管理播放状态
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

- [x] 10. 实现 View 层
  - [x] 10.1 实现主窗口布局
    - 创建 MainWindow.xaml，使用 Material Design 风格
    - 左侧分类导航面板 + 右侧视频列表区域
    - 顶部搜索栏
    - _Requirements: 4.6_

  - [x] 10.2 实现视频列表视图
    - 创建 VideoListView.xaml，使用 VirtualizingStackPanel
    - 显示缩略图、标题、时长等信息
    - 双击触发播放
    - _Requirements: 7.1, 2.3, 5.1_

  - [x] 10.3 实现导入对话框
    - 创建 ImportDialog.xaml
    - 文件夹选择、扫描结果列表、进度条
    - _Requirements: 1.2, 1.4_

  - [x] 10.4 实现视频播放器视图
    - 创建 VideoPlayerView.xaml，使用 MediaElement
    - 播放控制栏（播放/暂停/停止/音量/进度条）
    - 显示当前时间和总时长
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [x] 10.5 实现编辑对话框
    - 创建 EditDialog.xaml
    - 标题、描述编辑字段，Tag 选择器
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

  - [x] 10.6 实现分类管理面板
    - 创建 CategoryPanel.xaml
    - TreeView 显示分类树，Tag 列表
    - 新建/删除分类和 Tag 的操作按钮
    - _Requirements: 3.1, 3.4, 3.6, 3.7_

- [x] 11. 集成与连接
  - [x] 11.1 配置依赖注入
    - 在 App.xaml.cs 中配置 Microsoft.Extensions.DependencyInjection
    - 注册所有 Service、Repository、ViewModel
    - 配置 DbContext 生命周期
    - _Requirements: 8.1, 8.2_

  - [x] 11.2 实现应用启动逻辑
    - 首次启动时创建数据库和 Video_Library 目录
    - 检测 FFmpeg 可用性
    - _Requirements: 8.2_

- [x] 12. 最终检查点 - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

## 备注

- 标记 `*` 的任务为可选任务，可跳过以加速 MVP 开发
- 每个任务引用了具体的需求编号以确保可追溯性
- 检查点用于阶段性验证，确保增量开发的正确性
- 属性测试验证通用正确性属性，单元测试验证具体示例和边界情况
