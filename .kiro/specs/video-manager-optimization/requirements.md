# 需求文档：视频管理器优化

## 简介

本文档定义了现有 WPF 视频管理桌面应用的优化需求。优化涵盖四个方面：用户体验改进（右键菜单、实时搜索、排序、播放器快捷键、窗口记忆、批量操作）、性能提升（缩略图懒加载、数据库索引、并行元数据提取）、架构改进（提取 MainViewModel、IOptions 配置模式）、以及新功能添加（视频删除、系统播放器打开、标签颜色、文件系统监控）。

## 术语表

- **Video_Manager**：视频管理桌面应用程序主体
- **Video_Entry**：数据库中一条视频记录，包含视频文件路径、标题、描述、标签、时长、分辨率、文件大小、导入日期等元信息
- **Tag**：用户自定义的标签，用于对视频进行分类标记
- **Folder_Category**：用户创建的虚拟文件夹分类，用于组织视频
- **Video_List**：主界面中显示视频卡片的列表区域
- **Context_Menu**：视频卡片上的右键上下文菜单
- **Search_Engine**：应用内的搜索筛选引擎
- **Video_Player**：应用内置的视频播放器组件
- **MainViewModel**：主窗口的 ViewModel，负责顶层协调逻辑
- **Thumbnail_Loader**：缩略图懒加载器，负责异步加载和缓存缩略图
- **File_Watcher**：文件系统监控服务，检测视频文件的移动或删除
- **Window_Settings**：窗口尺寸和位置的持久化设置

## 需求

### 需求 1：视频列表右键上下文菜单扩展

**用户故事：** 作为用户，我希望在视频卡片上右键时能看到更多操作选项，以便快速执行常用操作而无需打开编辑对话框。

#### 验收标准

1. WHEN 用户右键点击 Video_List 中的视频卡片时，THE Context_Menu SHALL 显示以下菜单项：编辑信息、删除视频、复制文件路径、打开所在文件夹
2. WHEN 用户点击"编辑信息"菜单项时，THE Video_Manager SHALL 打开该视频的编辑对话框，加载当前视频的标题、描述和标签信息
3. WHEN 用户点击"复制文件路径"菜单项时，THE Video_Manager SHALL 将该视频的完整文件路径复制到系统剪贴板
4. WHEN 用户点击"删除视频"菜单项时，THE Video_Manager SHALL 显示确认对话框，提供"仅从库中移除"和"同时删除源文件"两个选项

### 需求 2：搜索实时建议与防抖

**用户故事：** 作为用户，我希望在搜索框输入时自动触发搜索，以便更快地找到目标视频而无需手动点击搜索按钮。

#### 验收标准

1. WHEN 用户在搜索框中输入文本时，THE Search_Engine SHALL 在用户停止输入 300 毫秒后自动执行搜索
2. WHEN 用户在防抖等待期间继续输入时，THE Search_Engine SHALL 重置防抖计时器，仅在最后一次输入后 300 毫秒执行搜索
3. WHEN 自动搜索正在执行时，如果用户输入了新的文本，THE Search_Engine SHALL 取消当前搜索请求并基于新文本重新开始防抖计时
4. WHEN 搜索框内容被清空时，THE Video_Manager SHALL 恢复显示完整的视频列表

### 需求 3：视频卡片排序

**用户故事：** 作为用户，我希望能够按不同字段对视频列表进行排序，以便按照自己的需求浏览视频。

#### 验收标准

1. THE Video_Manager SHALL 在视频列表区域提供排序选择控件，支持按时长、文件大小、导入日期三种字段排序
2. WHEN 用户选择一个排序字段时，THE Video_Manager SHALL 按该字段对当前视频列表进行升序或降序排列
3. WHEN 用户切换排序方向时，THE Video_Manager SHALL 反转当前排序结果的顺序
4. WHEN 排序条件改变时，THE Video_Manager SHALL 重置分页到第一页并重新加载数据

### 需求 4：播放器键盘快捷键

**用户故事：** 作为用户，我希望在播放视频时能使用键盘快捷键控制播放，以便更高效地浏览视频内容。

#### 验收标准

1. WHILE 视频正在播放或暂停时，WHEN 用户按下左方向键，THE Video_Player SHALL 将播放位置后退 5 秒
2. WHILE 视频正在播放或暂停时，WHEN 用户按下右方向键，THE Video_Player SHALL 将播放位置前进 5 秒
3. WHILE 视频已加载时，WHEN 用户按下空格键，THE Video_Player SHALL 切换播放/暂停状态
4. WHILE 视频已加载时，WHEN 用户按下速度控制快捷键时，THE Video_Player SHALL 在 0.5x、1.0x、1.5x、2.0x 之间循环切换播放速度
5. IF 快进或快退操作导致播放位置超出视频范围，THEN THE Video_Player SHALL 将位置限制在视频的起始或结束位置

### 需求 5：窗口尺寸和位置记忆

**用户故事：** 作为用户，我希望应用能记住我上次关闭时的窗口大小和位置，以便下次启动时恢复到相同的布局。

#### 验收标准

1. WHEN 用户关闭主窗口时，THE Video_Manager SHALL 将当前窗口的宽度、高度、左上角位置和窗口状态（正常/最大化）持久化到本地配置文件
2. WHEN 应用启动时，THE Video_Manager SHALL 从本地配置文件读取上次保存的窗口设置并恢复窗口尺寸和位置
3. IF 保存的窗口位置超出当前屏幕可用区域，THEN THE Video_Manager SHALL 将窗口重置到屏幕中央
4. IF 配置文件不存在或读取失败，THEN THE Video_Manager SHALL 使用默认窗口尺寸（1280×720）并居中显示

### 需求 6：批量操作

**用户故事：** 作为用户，我希望能够选择多个视频进行批量操作，以便高效地管理大量视频。

#### 验收标准

1. THE Video_Manager SHALL 支持在 Video_List 中通过 Ctrl+点击和 Shift+点击进行多选
2. WHEN 用户选中多个视频后执行批量删除时，THE Video_Manager SHALL 显示确认对话框，列出将被删除的视频数量，并提供"仅从库中移除"和"同时删除源文件"两个选项
3. WHEN 用户选中多个视频后执行批量标签分配时，THE Video_Manager SHALL 显示标签选择对话框，允许用户选择要添加的 Tag，并将所选 Tag 添加到所有选中的 Video_Entry
4. WHEN 用户选中多个视频后执行批量移动到分类时，THE Video_Manager SHALL 显示分类选择对话框，允许用户选择目标 Folder_Category，并将所有选中的 Video_Entry 添加到该分类
5. WHILE 批量操作正在执行时，THE Video_Manager SHALL 显示操作进度，并在完成后显示操作结果摘要

### 需求 7：缩略图懒加载集成

**用户故事：** 作为用户，我希望视频列表滚动时缩略图能流畅加载，以便在浏览大量视频时获得良好的体验。

#### 验收标准

1. WHEN Video_List 加载视频数据时，THE Thumbnail_Loader SHALL 仅对当前可视区域内的视频异步加载缩略图
2. WHEN 缩略图加载完成后，THE Thumbnail_Loader SHALL 将缩略图缓存在内存中，避免重复从磁盘读取
3. IF 缩略图文件不存在或加载失败，THEN THE Thumbnail_Loader SHALL 显示默认占位图标
4. THE Video_Manager SHALL 在 DI 容器中正确注册 Thumbnail_Loader 并将其注入到 VideoListViewModel 中

### 需求 8：数据库查询索引优化

**用户故事：** 作为用户，我希望搜索和排序操作响应迅速，以便在管理大量视频时保持流畅体验。

#### 验收标准

1. THE Video_Manager SHALL 在 VideoEntry 表的 FileSize 列上创建数据库索引
2. THE Video_Manager SHALL 在 VideoEntry 表的 Title 列上创建数据库索引（已存在，需验证）
3. THE Video_Manager SHALL 在 VideoEntry 表的 DurationTicks 列上创建数据库索引（已存在，需验证）
4. THE Video_Manager SHALL 在 VideoEntry 表的 ImportedAt 列上创建数据库索引（已存在，需验证）
5. WHEN 执行搜索或排序查询时，THE Search_Engine SHALL 利用数据库索引加速查询，避免全表扫描

### 需求 9：并行元数据提取

**用户故事：** 作为用户，我希望批量导入大量视频时元数据提取能并行执行，以便缩短导入等待时间。

#### 验收标准

1. WHEN 批量导入视频时，THE Video_Manager SHALL 使用并行处理同时提取多个视频的元数据和缩略图，最大并行度为处理器核心数
2. WHILE 并行导入正在执行时，THE Video_Manager SHALL 正确报告整体导入进度，包含已完成数量和总数量
3. IF 并行处理中某个视频的元数据提取失败，THEN THE Video_Manager SHALL 记录该失败并继续处理其他视频，失败不影响其他视频的导入

### 需求 10：提取 MainViewModel

**用户故事：** 作为开发者，我希望将 MainWindow.xaml.cs 中的协调逻辑提取到 MainViewModel 中，以便遵循 MVVM 模式，提高代码的可测试性和可维护性。

#### 验收标准

1. THE Video_Manager SHALL 创建 MainViewModel 类，将 MainWindow.xaml.cs 中的搜索触发、分页控制、导入后刷新、状态文本更新等协调逻辑迁移到该 ViewModel 中
2. WHEN MainViewModel 创建完成后，THE MainWindow.xaml.cs SHALL 仅保留纯 UI 事件处理（如打开对话框窗口），所有业务协调逻辑通过数据绑定和命令与 MainViewModel 交互
3. THE MainViewModel SHALL 通过依赖注入接收所有子 ViewModel 的引用，并在 DI 容器中正确注册

### 需求 11：IOptions 配置模式

**用户故事：** 作为开发者，我希望使用 IOptions&lt;T&gt; 配置模式替代 ImportService 的构造函数字符串参数，以便统一管理应用配置路径。

#### 验收标准

1. THE Video_Manager SHALL 定义 VideoManagerOptions 配置类，包含 VideoLibraryPath 和 ThumbnailDirectory 属性
2. THE Video_Manager SHALL 在 DI 容器中通过 IOptions&lt;VideoManagerOptions&gt; 注册配置，替代当前 ImportService 构造函数中的字符串参数
3. WHEN ImportService 需要访问路径配置时，THE ImportService SHALL 通过 IOptions&lt;VideoManagerOptions&gt; 获取配置值

### 需求 12：视频删除功能

**用户故事：** 作为用户，我希望能够从视频库中删除视频，并可选择是否同时删除源文件，以便清理不需要的视频。

#### 验收标准

1. WHEN 用户确认删除一个 Video_Entry 且选择"仅从库中移除"时，THE Video_Manager SHALL 从数据库中删除该 Video_Entry 记录及其所有标签和分类关联关系，保留视频文件和缩略图文件
2. WHEN 用户确认删除一个 Video_Entry 且选择"同时删除源文件"时，THE Video_Manager SHALL 从数据库中删除该 Video_Entry 记录及其所有关联关系，并删除对应的视频文件和缩略图文件
3. WHEN 删除操作完成后，THE Video_Manager SHALL 刷新视频列表以反映删除结果
4. IF 删除源文件时文件不存在或删除失败，THEN THE Video_Manager SHALL 仍然完成数据库记录的删除，并向用户显示文件删除失败的提示信息

### 需求 13：系统默认播放器打开

**用户故事：** 作为用户，我希望能够使用系统默认播放器打开视频，以便使用功能更丰富的外部播放器观看视频。

#### 验收标准

1. WHEN 用户在 Context_Menu 中点击"使用系统播放器打开"时，THE Video_Manager SHALL 使用操作系统默认关联的应用程序打开该视频文件
2. IF 视频文件不存在，THEN THE Video_Manager SHALL 显示文件不存在的错误提示

### 需求 14：标签颜色标记

**用户故事：** 作为用户，我希望能够为标签设置颜色，以便在视频列表中通过颜色快速区分不同类型的标签。

#### 验收标准

1. THE Tag 数据模型 SHALL 包含一个可选的 Color 属性，用于存储标签的十六进制颜色值
2. WHEN 用户创建或编辑 Tag 时，THE Video_Manager SHALL 提供颜色选择器，允许用户为 Tag 指定颜色
3. WHEN 在视频卡片或编辑界面中显示 Tag 时，THE Video_Manager SHALL 使用 Tag 的 Color 属性作为标签的背景色
4. IF Tag 未设置颜色，THEN THE Video_Manager SHALL 使用默认的主题颜色显示该 Tag

### 需求 15：文件系统监控

**用户故事：** 作为用户，我希望应用能自动检测视频文件被移动或删除的情况，以便及时了解库中视频的实际状态。

#### 验收标准

1. WHEN 应用启动时，THE File_Watcher SHALL 开始监控 Video_Library 目录中的文件变化
2. WHEN File_Watcher 检测到某个视频文件被删除时，THE Video_Manager SHALL 在对应的 Video_Entry 上标记文件缺失状态，并在视频列表中以视觉方式提示用户
3. WHEN File_Watcher 检测到某个视频文件被重命名时，THE Video_Manager SHALL 自动更新对应 Video_Entry 的文件路径
4. IF File_Watcher 初始化失败，THEN THE Video_Manager SHALL 记录错误日志并继续正常运行，文件监控功能降级为不可用
