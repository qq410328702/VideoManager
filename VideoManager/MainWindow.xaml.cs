using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using VideoManager.Models;
using VideoManager.Services;
using VideoManager.ViewModels;
using VideoManager.Views;

namespace VideoManager;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// Keeps only pure UI operations: dialog window creation, player window management.
/// All business coordination logic is handled by MainViewModel via data binding and commands.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _mainVm;
    private readonly IWindowSettingsService _windowSettingsService;

    public MainWindow(MainViewModel mainVm, IWindowSettingsService windowSettingsService)
    {
        InitializeComponent();

        _mainVm = mainVm;
        _windowSettingsService = windowSettingsService;

        // Load and apply window settings (size, position, maximized state)
        ApplyWindowSettings();

        // Set the MainViewModel as the window's DataContext for data binding
        DataContext = _mainVm;

        // Set DataContexts for child views
        CategoryPanelControl.DataContext = _mainVm.CategoryVm;
        VideoListControl.DataContext = _mainVm.VideoListVm;

        // Wire Import button click (pure UI: creates dialog window)
        ImportButton.Click += ImportButton_Click;

        // Wire video double-click for playback (pure UI: creates a new window)
        VideoListControl.VideoDoubleClicked += VideoListControl_VideoDoubleClicked;

        // Wire batch operation events (pure UI: creates dialog windows)
        VideoListControl.BatchDeleteRequested += VideoListControl_BatchDeleteRequested;
        VideoListControl.BatchTagRequested += VideoListControl_BatchTagRequested;
        VideoListControl.BatchCategoryRequested += VideoListControl_BatchCategoryRequested;

        // Wire context menu events (pure UI: creates dialog windows)
        VideoListControl.EditVideoRequested += VideoListControl_EditVideoRequested;
        VideoListControl.DeleteVideoRequested += VideoListControl_DeleteVideoRequested;

        // Wire search on Enter key (UI convenience, delegates to command)
        SearchKeywordTextBox.KeyDown += SearchKeywordTextBox_KeyDown;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    /// <summary>
    /// Loads persisted window settings and applies them.
    /// If no settings exist (first launch or corrupt file), uses defaults (1280×720, centered).
    /// </summary>
    private void ApplyWindowSettings()
    {
        var settings = _windowSettingsService.Load();

        if (settings is not null)
        {
            // Apply saved position and size
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = settings.Left;
            Top = settings.Top;
            Width = settings.Width;
            Height = settings.Height;

            if (settings.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        // else: XAML defaults apply (1280×720, CenterScreen)
    }

    /// <summary>
    /// Saves the current window state (position, size, maximized) when the window is closing.
    /// If the window is maximized, saves the RestoreBounds so that the normal size/position
    /// is preserved for the next non-maximized launch.
    /// </summary>
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        var isMaximized = WindowState == WindowState.Maximized;

        // When maximized, use RestoreBounds to capture the normal (non-maximized) position/size
        var bounds = isMaximized ? RestoreBounds : new Rect(Left, Top, Width, Height);

        var settings = new WindowSettings(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            isMaximized);

        _windowSettingsService.Save(settings);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Delegate initialization to MainViewModel
        if (_mainVm.InitializeCommand.CanExecute(null))
            await _mainVm.InitializeCommand.ExecuteAsync(null);
    }

    private void SearchKeywordTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (_mainVm.SearchCommand.CanExecute(null))
                _mainVm.SearchCommand.Execute(null);
        }
    }

    private async void VideoListControl_VideoDoubleClicked(object sender, RoutedEventArgs e)
    {
        if (_mainVm.VideoListVm.SelectedVideo is VideoEntry video)
        {
            // Get a fresh ViewModel so multiple windows don't share state
            var playerVm = App.ServiceProvider.GetRequiredService<VideoPlayerViewModel>();
            var playerView = new VideoPlayerView { DataContext = playerVm };

            var playerWindow = new Window
            {
                Title = $"播放 - {video.Title}",
                Content = playerView,
                Width = 900,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };
            playerWindow.Closed += (_, _) => playerVm.StopCommand.Execute(null);

            // Open video AFTER DataContext is set so the View receives PropertyChanged
            await playerVm.OpenVideoAsync(video);

            playerWindow.ShowDialog();
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var importVm = App.ServiceProvider.GetRequiredService<ImportViewModel>();
        var dialog = new ImportDialog(importVm)
        {
            Owner = this
        };

        dialog.ShowDialog();

        // Refresh video list after import via MainViewModel
        if (importVm.ImportResult != null && importVm.ImportResult.SuccessCount > 0)
        {
            if (_mainVm.RefreshCommand.CanExecute(null))
                await _mainVm.RefreshCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Handles edit video request from VideoListView context menu.
    /// Opens the EditDialog for the currently selected video. (Req 1.2)
    /// </summary>
    private async void VideoListControl_EditVideoRequested(object sender, RoutedEventArgs e)
    {
        if (_mainVm.VideoListVm.SelectedVideo is not VideoEntry video) return;

        try
        {
            using var scope = App.ServiceProvider.CreateScope();
            var editVm = scope.ServiceProvider.GetRequiredService<EditViewModel>();
            await editVm.LoadVideoAsync(video);

            var dialog = new EditDialog(editVm)
            {
                Owner = this
            };

            dialog.ShowDialog();

            // Refresh video list after editing to reflect any changes
            if (_mainVm.RefreshCommand.CanExecute(null))
                await _mainVm.RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开编辑对话框: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Handles delete video request from VideoListView context menu.
    /// Shows a confirmation dialog with options to remove from library only or also delete source files. (Req 1.4)
    /// </summary>
    private async void VideoListControl_DeleteVideoRequested(object sender, RoutedEventArgs e)
    {
        if (_mainVm.VideoListVm.SelectedVideo is not VideoEntry video) return;

        var confirmDialog = new ConfirmDeleteDialog(video.Title) { Owner = this };
        if (confirmDialog.ShowDialog() != true || confirmDialog.DeleteFile is null) return;

        var deleteFile = confirmDialog.DeleteFile.Value;

        try
        {
            using var scope = App.ServiceProvider.CreateScope();
            var deleteService = scope.ServiceProvider.GetRequiredService<IDeleteService>();

            var deleteResult = await deleteService.DeleteVideoAsync(video.Id, deleteFile, CancellationToken.None);

            if (deleteResult.Success)
            {
                if (deleteResult.ErrorMessage is not null)
                {
                    MessageBox.Show(
                        $"视频已从库中移除，但文件删除时出现问题：\n{deleteResult.ErrorMessage}",
                        "部分完成",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                if (_mainVm.RefreshCommand.CanExecute(null))
                    await _mainVm.RefreshCommand.ExecuteAsync(null);
            }
            else
            {
                MessageBox.Show(
                    $"删除失败: {deleteResult.ErrorMessage}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Handles batch delete request from VideoListView.
    /// Shows a confirmation dialog with options to remove from library only or also delete source files.
    /// </summary>
    private async void VideoListControl_BatchDeleteRequested(object sender, RoutedEventArgs e)
    {
        var videoListVm = _mainVm.VideoListVm;
        var selectedIds = videoListVm.GetSelectedVideoIds();
        if (selectedIds.Count == 0) return;

        var confirmDialog = new ConfirmDeleteDialog(selectedIds.Count) { Owner = this };
        if (confirmDialog.ShowDialog() != true || confirmDialog.DeleteFile is null) return;

        var deleteFiles = confirmDialog.DeleteFile.Value;

        videoListVm.IsBatchOperating = true;
        videoListVm.BatchProgressText = "正在批量删除...";

        try
        {
            using var scope = App.ServiceProvider.CreateScope();
            var deleteService = scope.ServiceProvider.GetRequiredService<IDeleteService>();

            var progress = new Progress<BatchProgress>(p =>
            {
                videoListVm.BatchProgressText = $"正在删除... ({p.Completed}/{p.Total})";
            });

            var deleteResult = await deleteService.BatchDeleteAsync(selectedIds, deleteFiles, progress, CancellationToken.None);

            // Show result summary (Req 6.5)
            var summaryMessage = $"批量删除完成\n\n成功: {deleteResult.SuccessCount} 个\n失败: {deleteResult.FailCount} 个";
            if (deleteResult.Errors.Count > 0)
            {
                summaryMessage += "\n\n失败详情:\n" + string.Join("\n",
                    deleteResult.Errors.Select(err => $"  视频 {err.VideoId}: {err.Reason}"));
            }

            MessageBox.Show(summaryMessage, "批量删除结果", MessageBoxButton.OK,
                deleteResult.FailCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            // Refresh video list after deletion
            if (_mainVm.RefreshCommand.CanExecute(null))
                await _mainVm.RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"批量删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            videoListVm.IsBatchOperating = false;
            videoListVm.BatchProgressText = string.Empty;
        }
    }

    /// <summary>
    /// Handles batch tag request from VideoListView.
    /// Shows a tag selection dialog and applies selected tags to all selected videos.
    /// </summary>
    private async void VideoListControl_BatchTagRequested(object sender, RoutedEventArgs e)
    {
        var videoListVm = _mainVm.VideoListVm;
        var selectedIds = videoListVm.GetSelectedVideoIds();
        if (selectedIds.Count == 0) return;

        var availableTags = _mainVm.CategoryVm.Tags;
        if (availableTags.Count == 0)
        {
            MessageBox.Show("没有可用的标签。请先在分类面板中创建标签。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new BatchTagDialog(availableTags, selectedIds.Count)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true) return;

        videoListVm.IsBatchOperating = true;
        videoListVm.BatchProgressText = "正在批量添加标签...";

        try
        {
            using var scope = App.ServiceProvider.CreateScope();
            var editService = scope.ServiceProvider.GetRequiredService<IEditService>();

            var totalOps = dialog.SelectedTags.Count;
            var completedOps = 0;

            foreach (var tag in dialog.SelectedTags)
            {
                await editService.BatchAddTagAsync(selectedIds, tag.Id, CancellationToken.None);
                completedOps++;
                videoListVm.BatchProgressText = $"正在添加标签... ({completedOps}/{totalOps})";
            }

            // Show result summary (Req 6.5)
            MessageBox.Show(
                $"批量标签添加完成\n\n已为 {selectedIds.Count} 个视频添加了 {dialog.SelectedTags.Count} 个标签",
                "批量标签结果",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Refresh video list
            if (_mainVm.RefreshCommand.CanExecute(null))
                await _mainVm.RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"批量标签添加失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            videoListVm.IsBatchOperating = false;
            videoListVm.BatchProgressText = string.Empty;
        }
    }

    /// <summary>
    /// Handles batch category request from VideoListView.
    /// Shows a category selection dialog and moves all selected videos to the chosen category.
    /// </summary>
    private async void VideoListControl_BatchCategoryRequested(object sender, RoutedEventArgs e)
    {
        var videoListVm = _mainVm.VideoListVm;
        var selectedIds = videoListVm.GetSelectedVideoIds();
        if (selectedIds.Count == 0) return;

        var categories = _mainVm.CategoryVm.Categories;
        if (categories.Count == 0)
        {
            MessageBox.Show("没有可用的分类。请先在分类面板中创建分类。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new BatchCategoryDialog(categories, selectedIds.Count)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.SelectedCategory is null) return;

        videoListVm.IsBatchOperating = true;
        videoListVm.BatchProgressText = "正在批量移动到分类...";

        try
        {
            using var scope = App.ServiceProvider.CreateScope();
            var editService = scope.ServiceProvider.GetRequiredService<IEditService>();

            await editService.BatchMoveToCategoryAsync(selectedIds, dialog.SelectedCategory.Id, CancellationToken.None);

            // Show result summary (Req 6.5)
            MessageBox.Show(
                $"批量分类移动完成\n\n已将 {selectedIds.Count} 个视频移动到分类 \"{dialog.SelectedCategory.Name}\"",
                "批量分类结果",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Refresh video list
            if (_mainVm.RefreshCommand.CanExecute(null))
                await _mainVm.RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"批量分类移动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            videoListVm.IsBatchOperating = false;
            videoListVm.BatchProgressText = string.Empty;
        }
    }
}
