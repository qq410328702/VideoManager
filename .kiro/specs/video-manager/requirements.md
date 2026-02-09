# 需求文档

## 简介

本文档定义了一个基于 C# + WPF 的视频管理桌面应用的功能需求。该应用旨在帮助用户高效管理上万个本地视频文件，提供统一存储、分类管理、搜索筛选、缩略图预览、视频播放和元信息编辑等核心功能。技术栈包括 WPF + Material Design In XAML Toolkit（UI）、SQLite + Entity Framework Core（数据存储）、FFmpeg（视频处理）。

## 术语表

- **Video_Manager**：视频管理桌面应用程序主体
- **Video_Library**：应用管理的专用视频存储目录，所有导入的视频统一存放于此
- **Video_Entry**：数据库中一条视频记录，包含视频文件路径、标题、描述、标签、时长、分辨率、文件大小、导入日期等元信息
- **Tag**：用户自定义的标签，用于对视频进行分类标记
- **Folder_Category**：用户创建的虚拟文件夹分类，用于组织视频
- **Thumbnail**：通过 FFmpeg 从视频中提取的预览缩略图
- **Import_Task**：批量导入视频的后台任务，包含扫描、复制/移动、元信息提取、缩略图生成等步骤
- **Search_Engine**：应用内的搜索筛选引擎，支持按多种条件组合查询视频

## 需求

### 需求 1：批量视频导入

**用户故事：** 作为用户，我希望能够批量扫描文件夹并导入视频到应用管理的专用目录中，以便统一管理所有视频文件。

#### 验收标准

1. WHEN 用户选择一个或多个源文件夹进行扫描时，THE Video_Manager SHALL 递归扫描所有子文件夹，识别所有支持格式（MP4、AVI、MKV、MOV、WMV）的视频文件
2. WHEN 扫描完成后，THE Video_Manager SHALL 显示待导入视频列表，包含文件名、文件大小、时长信息，并允许用户选择要导入的视频
3. WHEN 用户确认导入时，THE Video_Manager SHALL 将选中的视频文件复制或移动到 Video_Library 目录中，并在数据库中创建对应的 Video_Entry 记录
4. WHILE Import_Task 正在执行时，THE Video_Manager SHALL 显示导入进度条，包含已完成数量、总数量和当前处理的文件名
5. WHILE Import_Task 正在执行时，THE Video_Manager SHALL 保持界面响应，允许用户继续浏览已有视频
6. IF 导入过程中某个视频文件复制失败，THEN THE Video_Manager SHALL 记录失败原因，跳过该文件继续处理剩余文件，并在导入完成后显示失败文件汇总
7. IF 待导入的视频文件与 Video_Library 中已有文件重名，THEN THE Video_Manager SHALL 自动重命名新文件以避免冲突，并在 Video_Entry 中记录原始文件名
8. WHEN 视频文件成功复制到 Video_Library 后，THE Video_Manager SHALL 使用 FFmpeg 提取视频元信息（时长、分辨率、编码格式、比特率）并存储到 Video_Entry 中

### 需求 2：缩略图生成

**用户故事：** 作为用户，我希望每个视频都有缩略图预览，以便快速浏览和识别视频内容。

#### 验收标准

1. WHEN 一个视频文件成功导入后，THE Video_Manager SHALL 使用 FFmpeg 从视频中提取一帧画面作为 Thumbnail，并将缩略图文件存储在应用管理的缩略图目录中
2. IF FFmpeg 生成缩略图失败，THEN THE Video_Manager SHALL 使用默认占位图作为该视频的 Thumbnail，并记录错误日志
3. WHEN 显示视频列表时，THE Video_Manager SHALL 异步加载 Thumbnail，避免阻塞界面渲染
4. THE Video_Manager SHALL 将 Thumbnail 文件路径存储在对应的 Video_Entry 记录中

### 需求 3：分类管理

**用户故事：** 作为用户，我希望能够通过标签和文件夹分类来组织视频，以便按照自己的方式管理视频集合。

#### 验收标准

1. WHEN 用户创建一个新的 Tag 时，THE Video_Manager SHALL 在数据库中创建该 Tag 记录，Tag 名称在系统内唯一
2. WHEN 用户为一个 Video_Entry 添加一个或多个 Tag 时，THE Video_Manager SHALL 在数据库中建立 Video_Entry 与 Tag 的关联关系
3. WHEN 用户从一个 Video_Entry 移除某个 Tag 时，THE Video_Manager SHALL 删除该关联关系，但保留 Tag 本身和 Video_Entry 本身
4. WHEN 用户创建一个新的 Folder_Category 时，THE Video_Manager SHALL 在数据库中创建该分类记录，支持多级嵌套的树形结构
5. WHEN 用户将一个 Video_Entry 添加到某个 Folder_Category 时，THE Video_Manager SHALL 在数据库中建立关联关系，同一个 Video_Entry 可以属于多个 Folder_Category
6. WHEN 用户删除一个 Folder_Category 时，THE Video_Manager SHALL 仅删除该分类记录及其关联关系，Video_Entry 和实际视频文件保持不变
7. WHEN 用户删除一个 Tag 时，THE Video_Manager SHALL 删除该 Tag 记录及所有与 Video_Entry 的关联关系，Video_Entry 本身保持不变

### 需求 4：搜索与筛选

**用户故事：** 作为用户，我希望能够按多种条件搜索和筛选视频，以便快速找到需要的视频。

#### 验收标准

1. WHEN 用户输入搜索关键词时，THE Search_Engine SHALL 在视频标题和描述字段中进行模糊匹配，并返回匹配的 Video_Entry 列表
2. WHEN 用户选择按 Tag 筛选时，THE Search_Engine SHALL 返回包含所选 Tag 的所有 Video_Entry
3. WHEN 用户设置日期范围筛选条件时，THE Search_Engine SHALL 返回导入日期在指定范围内的所有 Video_Entry
4. WHEN 用户设置时长范围筛选条件时，THE Search_Engine SHALL 返回视频时长在指定范围内的所有 Video_Entry
5. WHEN 用户同时设置多个筛选条件时，THE Search_Engine SHALL 对所有条件取交集，返回同时满足所有条件的 Video_Entry
6. WHEN 搜索结果为空时，THE Video_Manager SHALL 显示明确的空结果提示信息

### 需求 5：视频播放

**用户故事：** 作为用户，我希望能够在应用内直接播放视频，以便无需切换到外部播放器即可观看视频内容。

#### 验收标准

1. WHEN 用户双击一个 Video_Entry 时，THE Video_Manager SHALL 在内置播放器中打开并播放该视频
2. WHILE 视频正在播放时，THE Video_Manager SHALL 提供播放、暂停、停止、音量调节和进度拖动控制
3. WHILE 视频正在播放时，THE Video_Manager SHALL 显示当前播放时间和视频总时长
4. IF 视频文件损坏或格式不支持，THEN THE Video_Manager SHALL 显示明确的错误提示信息，说明无法播放的原因

### 需求 6：视频元信息编辑

**用户故事：** 作为用户，我希望能够编辑视频的元信息（标题、描述、标签等），以便更好地组织和描述视频内容。

#### 验收标准

1. WHEN 用户打开 Video_Entry 的编辑界面时，THE Video_Manager SHALL 显示当前的标题、描述和已关联的 Tag 列表
2. WHEN 用户修改标题或描述并保存时，THE Video_Manager SHALL 将更新后的信息持久化到数据库中的 Video_Entry 记录
3. WHEN 用户在编辑界面添加或移除 Tag 时，THE Video_Manager SHALL 实时更新数据库中的关联关系
4. IF 用户将标题设置为空字符串，THEN THE Video_Manager SHALL 拒绝保存并提示标题不能为空

### 需求 7：大规模数据性能优化

**用户故事：** 作为用户，我希望在管理上万个视频时应用依然流畅响应，以便获得良好的使用体验。

#### 验收标准

1. WHEN 显示视频列表时，THE Video_Manager SHALL 使用虚拟化列表技术，仅渲染当前可视区域内的列表项
2. WHEN 加载视频列表数据时，THE Video_Manager SHALL 使用分页查询，每次从数据库加载固定数量的记录
3. WHEN 加载 Thumbnail 时，THE Video_Manager SHALL 使用异步加载和内存缓存策略，避免重复读取磁盘
4. WHILE 执行数据库查询时，THE Video_Manager SHALL 在后台线程执行查询操作，保持 UI 线程响应

### 需求 8：数据持久化

**用户故事：** 作为用户，我希望所有视频元信息和分类数据可靠地存储在本地数据库中，以便数据不会丢失。

#### 验收标准

1. THE Video_Manager SHALL 使用 SQLite 数据库通过 Entity Framework Core 存储所有 Video_Entry、Tag、Folder_Category 及其关联关系
2. WHEN 应用首次启动时，THE Video_Manager SHALL 自动创建数据库文件和所有必要的表结构
3. WHEN 数据库结构需要升级时，THE Video_Manager SHALL 通过 Entity Framework Core 的迁移机制自动完成数据库升级
4. IF 数据库操作发生异常，THEN THE Video_Manager SHALL 回滚当前事务并向用户显示错误信息
