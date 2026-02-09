# VideoManager

一个基于 WPF 的本地视频管理工具，帮助你整理、分类和播放本地视频文件。

## 功能

- **视频导入** — 扫描文件夹，批量导入视频到库中（支持复制/移动模式）
- **分类管理** — 树形文件夹分类结构，支持多级嵌套
- **标签系统** — 自定义标签及颜色，灵活标记视频
- **搜索与排序** — 按标题、标签、分类等条件搜索，多种排序方式
- **视频播放** — 内置播放器，支持播放速度调节、快进快退
- **批量操作** — 批量删除、批量打标签、批量移动分类
- **视频编辑** — 编辑标题、描述、标签和分类
- **缩略图缓存** — 自动生成并缓存视频缩略图
- **文件监控** — 实时检测视频文件变动
- **窗口记忆** — 记住窗口大小和位置

## 下载

前往 [Releases](https://github.com/qq410328702/VideoManager/releases) 页面下载最新版本。

下载 `VideoManager.exe` 即可直接运行，无需安装 .NET 运行时。

## 运行要求

- Windows 10/11 x64

## 从源码构建

```bash
# 克隆仓库
git clone https://github.com/qq410328702/VideoManager.git
cd VideoManager

# 调试运行
dotnet run --project VideoManager/VideoManager.csproj

# 发布单文件 exe
dotnet publish VideoManager/VideoManager.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true -o ./publish
```

## 技术栈

- .NET 8 / WPF
- Material Design In XAML Toolkit
- CommunityToolkit.Mvvm (MVVM 框架)
- Entity Framework Core + SQLite
- Microsoft.Extensions.DependencyInjection

## 项目结构

```
VideoManager/
├── Models/          # 数据模型
├── Data/            # EF Core DbContext
├── Repositories/    # 数据访问层
├── Services/        # 业务逻辑层
├── ViewModels/      # MVVM ViewModel
├── Views/           # WPF 视图和对话框
└── Migrations/      # 数据库迁移

VideoManager.Tests/  # 单元测试和属性测试
```

## 许可证

MIT
