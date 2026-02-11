using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.ViewModels;

/// <summary>
/// ViewModel responsible for batch operation logic, extracted from MainViewModel.
/// Manages batch delete, batch tag, and batch category operations.
/// Integrates BatchChunkProcessor for chunked processing and communicates
/// results via WeakReferenceMessenger.
/// </summary>
public partial class BatchOperationViewModel : ViewModelBase
{
    private readonly VideoListViewModel _videoListVm;
    private readonly CategoryViewModel _categoryVm;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;

    public BatchOperationViewModel(
        VideoListViewModel videoListVm,
        CategoryViewModel categoryVm,
        IDialogService dialogService,
        IServiceProvider serviceProvider)
    {
        _videoListVm = videoListVm ?? throw new ArgumentNullException(nameof(videoListVm));
        _categoryVm = categoryVm ?? throw new ArgumentNullException(nameof(categoryVm));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// Batch deletes selected videos after confirmation.
    /// Creates a backup before executing the batch delete.
    /// Uses BatchChunkProcessor for chunked processing.
    /// </summary>
    [RelayCommand]
    private async Task BatchDeleteAsync()
    {
        var selectedIds = _videoListVm.GetSelectedVideoIds();
        if (selectedIds.Count == 0) return;

        var confirmResult = await _dialogService.ShowBatchDeleteConfirmAsync(selectedIds.Count);
        if (confirmResult is null) return;

        var deleteFiles = confirmResult.Value.DeleteFile;

        var ct = _videoListVm.BeginBatchOperation();
        _videoListVm.BatchProgressText = "正在批量删除...";

        try
        {
            // Create backup before batch delete
            try
            {
                var backupService = _serviceProvider.GetRequiredService<IBackupService>();
                _videoListVm.BatchProgressText = "正在创建备份...";
                await backupService.CreateBackupAsync(CancellationToken.None);
            }
            catch (Exception backupEx)
            {
                Trace.TraceWarning($"Pre-delete backup failed: {backupEx.Message}");
            }

            _videoListVm.BatchProgressText = "正在批量删除...";

            using var scope = _serviceProvider.CreateScope();
            var deleteService = scope.ServiceProvider.GetRequiredService<IDeleteService>();

            var successCount = 0;
            var failCount = 0;
            var errors = new List<DeleteError>();

            await BatchChunkProcessor.ProcessInChunksAsync(
                selectedIds,
                async (chunk, token) =>
                {
                    var chunkResult = await deleteService.BatchDeleteAsync(
                        chunk.ToList(), deleteFiles, null, token);
                    successCount += chunkResult.SuccessCount;
                    failCount += chunkResult.FailCount;
                    errors.AddRange(chunkResult.Errors);
                },
                new Progress<(int completed, int total)>(p =>
                {
                    _videoListVm.BatchProgressPercentage = (double)p.completed / p.total * 100;
                    _videoListVm.BatchProgressText = $"正在删除... ({p.completed}/{p.total})";
                }),
                ct);

            // Show result summary
            var summaryMessage = $"批量删除完成\n\n成功: {successCount} 个\n失败: {failCount} 个";
            if (errors.Count > 0)
            {
                summaryMessage += "\n\n失败详情:\n" + string.Join("\n",
                    errors.Select(err => $"  视频 {err.VideoId}: {err.Reason}"));
            }

            _dialogService.ShowMessage(summaryMessage, "批量删除结果",
                failCount > 0 ? MessageLevel.Warning : MessageLevel.Information);

            WeakReferenceMessenger.Default.Send(
                new BatchOperationCompletedMessage("BatchDelete", successCount, failCount));
            WeakReferenceMessenger.Default.Send(new RefreshRequestedMessage());
        }
        catch (OperationCanceledException)
        {
            _dialogService.ShowMessage("批量删除已取消。已完成的删除操作不会回滚。", "已取消", MessageLevel.Information);
            WeakReferenceMessenger.Default.Send(new RefreshRequestedMessage());
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessage($"批量删除失败: {ex.Message}", "错误", MessageLevel.Error);
        }
        finally
        {
            _videoListVm.EndBatchOperation();
        }
    }

    /// <summary>
    /// Batch adds tags to selected videos.
    /// Uses BatchChunkProcessor for chunked processing.
    /// </summary>
    [RelayCommand]
    private async Task BatchTagAsync()
    {
        var selectedIds = _videoListVm.GetSelectedVideoIds();
        if (selectedIds.Count == 0) return;

        var availableTags = _categoryVm.Tags;
        if (availableTags.Count == 0)
        {
            _dialogService.ShowMessage("没有可用的标签。请先在分类面板中创建标签。", "提示", MessageLevel.Information);
            return;
        }

        var selectedTags = await _dialogService.ShowBatchTagDialogAsync(availableTags, selectedIds.Count);
        if (selectedTags is null) return;

        var ct = _videoListVm.BeginBatchOperation();
        _videoListVm.BatchProgressText = "正在批量添加标签...";

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var editService = scope.ServiceProvider.GetRequiredService<IEditService>();

            var completedTags = 0;
            var totalTags = selectedTags.Count;

            // For each selected tag, use BatchChunkProcessor to process video IDs in chunks
            foreach (var tag in selectedTags)
            {
                ct.ThrowIfCancellationRequested();

                await BatchChunkProcessor.ProcessInChunksAsync(
                    selectedIds,
                    async (chunk, token) =>
                    {
                        await editService.BatchAddTagAsync(chunk.ToList(), tag.Id, token);
                    },
                    null,
                    ct);

                completedTags++;
                _videoListVm.BatchProgressPercentage = (double)completedTags / totalTags * 100;
                _videoListVm.BatchProgressText = $"正在添加标签... ({completedTags}/{totalTags})";
            }

            _dialogService.ShowMessage(
                $"批量标签添加完成\n\n已为 {selectedIds.Count} 个视频添加了 {selectedTags.Count} 个标签",
                "批量标签结果",
                MessageLevel.Information);

            WeakReferenceMessenger.Default.Send(
                new BatchOperationCompletedMessage("BatchTag", selectedIds.Count, 0));
            WeakReferenceMessenger.Default.Send(new RefreshRequestedMessage());
        }
        catch (OperationCanceledException)
        {
            _dialogService.ShowMessage("批量标签添加已取消。已完成的标签操作不会回滚。", "已取消", MessageLevel.Information);
            WeakReferenceMessenger.Default.Send(new RefreshRequestedMessage());
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessage($"批量标签添加失败: {ex.Message}", "错误", MessageLevel.Error);
        }
        finally
        {
            _videoListVm.EndBatchOperation();
        }
    }

    /// <summary>
    /// Batch moves selected videos to a category.
    /// Uses BatchChunkProcessor for chunked processing.
    /// </summary>
    [RelayCommand]
    private async Task BatchCategoryAsync()
    {
        var selectedIds = _videoListVm.GetSelectedVideoIds();
        if (selectedIds.Count == 0) return;

        var categories = _categoryVm.Categories;
        if (categories.Count == 0)
        {
            _dialogService.ShowMessage("没有可用的分类。请先在分类面板中创建分类。", "提示", MessageLevel.Information);
            return;
        }

        var selectedCategory = await _dialogService.ShowBatchCategoryDialogAsync(categories, selectedIds.Count);
        if (selectedCategory is null) return;

        var ct = _videoListVm.BeginBatchOperation();
        _videoListVm.BatchProgressText = "正在批量移动到分类...";

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var editService = scope.ServiceProvider.GetRequiredService<IEditService>();

            await BatchChunkProcessor.ProcessInChunksAsync(
                selectedIds,
                async (chunk, token) =>
                {
                    await editService.BatchMoveToCategoryAsync(chunk.ToList(), selectedCategory.Id, token);
                },
                new Progress<(int completed, int total)>(p =>
                {
                    _videoListVm.BatchProgressPercentage = (double)p.completed / p.total * 100;
                    _videoListVm.BatchProgressText = $"正在移动分类... ({p.completed}/{p.total})";
                }),
                ct);

            _dialogService.ShowMessage(
                $"批量分类移动完成\n\n已将 {selectedIds.Count} 个视频移动到分类 \"{selectedCategory.Name}\"",
                "批量分类结果",
                MessageLevel.Information);

            WeakReferenceMessenger.Default.Send(
                new BatchOperationCompletedMessage("BatchCategory", selectedIds.Count, 0));
            WeakReferenceMessenger.Default.Send(new RefreshRequestedMessage());
        }
        catch (OperationCanceledException)
        {
            _dialogService.ShowMessage("批量分类移动已取消。已完成的分类操作不会回滚。", "已取消", MessageLevel.Information);
            WeakReferenceMessenger.Default.Send(new RefreshRequestedMessage());
        }
        catch (Exception ex)
        {
            _dialogService.ShowMessage($"批量分类移动失败: {ex.Message}", "错误", MessageLevel.Error);
        }
        finally
        {
            _videoListVm.EndBatchOperation();
        }
    }
}
