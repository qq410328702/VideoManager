using VideoManager.Models;

namespace VideoManager.ViewModels;

// 分页消息
public record PageChangedMessage(int NewPage);
public record PageInfoUpdatedMessage(int CurrentPage, int TotalPages, int TotalCount);

// 排序消息
public record SortChangedMessage(SortField Field, SortDirection Direction);

// 批量操作消息
public record BatchOperationCompletedMessage(string OperationType, int SuccessCount, int FailCount);
public record RefreshRequestedMessage;
