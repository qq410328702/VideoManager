# 需求文档：视频管理器优化 V2

## 简介

本文档定义了 VideoManager 应用的第二轮优化需求。在第一轮优化（架构重构、MVVM 提取、基础功能完善）的基础上，本轮聚焦于数据库查询性能、结构化日志、输入验证统一、瞬态故障重试、批量数据库操作、软删除机制等方面的深度优化。

## 术语表

- **Video_Manager**：视频管理桌面应用程序主体
- **Video_Entry**：数据库中一条视频记录
- **Search_Service**：搜索服务，负责构建和执行视频查询
- **Import_Service**：导入服务，负责视频文件的扫描、复制和元数据提取
- **Delete_Service**：删除服务，负责视频的数据库删除和文件删除
- **DbContext**：EF Core 数据库上下文
- **Change_Tracker**：EF Core 的变更追踪器，用于跟踪实体状态变化
- **FFmpeg**：外部视频处理工具，用于元数据提取和缩略图生成
- **Polly**：.NET 弹性和瞬态故障处理库
- **FTS5**：SQLite 全文搜索扩展模块

## 需求

### 需求 1：数据库查询优化 — AsNoTracking

**用户故事：** 作为用户，我希望视频列表加载和搜索操作更快速，以便在管理大量视频时获得流畅体验。

#### 验收标准

1. WHEN Search_Service 执行只读查询（搜索、列表加载）时，THE Search_Service SHALL 对查询添加 `.AsNoTracking()` 以跳过 Change_Tracker，减少内存开销和查询耗时
2. WHEN 使用 AsNoTracking 查询返回的实体时，THE Video_Manager SHALL 确保这些实体不会被用于后续的更新操作（因为它们不被 Change_Tracker 追踪）
3. THE Search_Service SHALL 在查询中保留 `.Include(v => v.Tags).Include(v => v.Categories)` 以确保关联数据正常加载

### 需求 2：结构化日志

**用户故事：** 作为开发者，我希望应用使用结构化日志框架替代 Trace，以便更高效地排查问题和监控应用运行状态。

#### 验收标准

1. THE Video_Manager SHALL 引入 `Microsoft.Extensions.Logging` 作为日志框架，并在 DI 容器中注册日志服务
2. THE Video_Manager SHALL 为所有 Service 类注入 `ILogger<T>`，替代现有的 `Trace.TraceError` 和 `Trace.TraceWarning` 调用
3. WHEN 记录日志时，THE Video_Manager SHALL 使用适当的日志级别：Error 用于异常和失败，Warning 用于降级场景，Information 用于关键业务操作（如导入完成、删除完成），Debug 用于详细调试信息
4. THE Video_Manager SHALL 配置日志输出到 Debug 控制台，以便开发时查看日志

### 需求 3：输入验证统一

**用户故事：** 作为开发者，我希望输入验证逻辑集中在模型层，以便保持验证规则的一致性和可维护性。

#### 验收标准

1. THE VideoEntry 模型 SHALL 使用 Data Annotations 标注必填字段：Title 标注 `[Required]` 和 `[StringLength(500)]`，FileName 标注 `[Required]`
2. THE Tag 模型 SHALL 使用 Data Annotations 标注：Name 标注 `[Required]` 和 `[StringLength(100)]`，Color 标注 `[StringLength(9)]`（格式 #RRGGBBAA）
3. THE Video_Manager 的 Service 层 SHALL 在执行写入操作前调用验证逻辑，确保实体满足 Data Annotations 约束
4. WHEN 验证失败时，THE Video_Manager SHALL 抛出包含具体验证错误信息的异常

### 需求 4：瞬态故障重试

**用户故事：** 作为用户，我希望视频导入时遇到临时性故障能自动重试，以便减少因偶发错误导致的导入失败。

#### 验收标准

1. THE Video_Manager SHALL 引入 Polly 库用于瞬态故障处理
2. WHEN Import_Service 调用 FFmpeg 提取元数据失败时，THE Import_Service SHALL 自动重试最多 2 次，每次重试间隔递增（1 秒、2 秒）
3. WHEN Import_Service 调用 FFmpeg 生成缩略图失败时，THE Import_Service SHALL 自动重试最多 2 次，每次重试间隔递增
4. IF 重试全部失败，THEN THE Import_Service SHALL 使用默认元数据（时长为零、分辨率为零）继续导入，并记录 Warning 级别日志
5. THE 重试策略 SHALL 仅对非取消异常（非 OperationCanceledException）进行重试

### 需求 5：Repository 批量操作

**用户故事：** 作为用户，我希望批量导入大量视频时数据库写入更高效，以便缩短导入等待时间。

#### 验收标准

1. THE IVideoRepository SHALL 新增 `AddRangeAsync` 方法，支持一次性批量添加多个 Video_Entry
2. WHEN Import_Service 完成所有文件的元数据提取后，THE Import_Service SHALL 使用 `AddRangeAsync` 批量写入数据库，替代当前逐条 `AddAsync` 的方式
3. THE `AddRangeAsync` 方法 SHALL 在单次 `SaveChangesAsync` 调用中提交所有实体，减少数据库往返次数
4. IF 批量写入过程中发生异常，THEN THE Import_Service SHALL 回退到逐条写入模式，确保尽可能多的视频成功导入

### 需求 6：软删除机制

**用户故事：** 作为用户，我希望删除的视频能够被恢复，以便在误删时找回视频记录。

#### 验收标准

1. THE VideoEntry 模型 SHALL 新增 `IsDeleted`（bool，默认 false）和 `DeletedAt`（DateTime?，默认 null）属性
2. THE DbContext SHALL 配置全局查询过滤器 `HasQueryFilter(v => !v.IsDeleted)`，使所有常规查询自动排除已软删除的记录
3. WHEN 用户执行删除操作时，THE Delete_Service SHALL 将 Video_Entry 的 `IsDeleted` 设为 true、`DeletedAt` 设为当前 UTC 时间，而非物理删除数据库记录
4. WHEN 用户选择"同时删除源文件"时，THE Delete_Service SHALL 在软删除数据库记录的同时删除视频文件和缩略图文件
5. THE Delete_Service SHALL 清除被软删除视频的标签和分类关联关系
