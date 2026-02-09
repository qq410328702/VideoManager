using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoManager.Models;
using VideoManager.ViewModels;

namespace VideoManager.Views;

/// <summary>
/// Code-behind for VideoListView. Kept minimal — only event handlers
/// that cannot be expressed purely in XAML.
/// </summary>
public partial class VideoListView : UserControl
{
    /// <summary>
    /// Routed event raised when the user double-clicks a video item.
    /// Parent controls (e.g. MainWindow) can subscribe to trigger playback.
    /// </summary>
    public static readonly RoutedEvent VideoDoubleClickedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(VideoDoubleClicked),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(VideoListView));

    /// <summary>
    /// Occurs when a video item is double-clicked.
    /// </summary>
    public event RoutedEventHandler VideoDoubleClicked
    {
        add => AddHandler(VideoDoubleClickedEvent, value);
        remove => RemoveHandler(VideoDoubleClickedEvent, value);
    }

    /// <summary>
    /// Routed event raised when the user clicks the batch delete button.
    /// Parent controls (e.g. MainWindow) can subscribe to show the delete confirmation dialog.
    /// </summary>
    public static readonly RoutedEvent BatchDeleteRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(BatchDeleteRequested),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(VideoListView));

    /// <summary>
    /// Occurs when batch delete is requested.
    /// </summary>
    public event RoutedEventHandler BatchDeleteRequested
    {
        add => AddHandler(BatchDeleteRequestedEvent, value);
        remove => RemoveHandler(BatchDeleteRequestedEvent, value);
    }

    /// <summary>
    /// Routed event raised when the user clicks the batch tag button.
    /// Parent controls (e.g. MainWindow) can subscribe to show the tag selection dialog.
    /// </summary>
    public static readonly RoutedEvent BatchTagRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(BatchTagRequested),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(VideoListView));

    /// <summary>
    /// Occurs when batch tag assignment is requested.
    /// </summary>
    public event RoutedEventHandler BatchTagRequested
    {
        add => AddHandler(BatchTagRequestedEvent, value);
        remove => RemoveHandler(BatchTagRequestedEvent, value);
    }

    /// <summary>
    /// Routed event raised when the user clicks the batch category button.
    /// Parent controls (e.g. MainWindow) can subscribe to show the category selection dialog.
    /// </summary>
    public static readonly RoutedEvent BatchCategoryRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(BatchCategoryRequested),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(VideoListView));

    /// <summary>
    /// Occurs when batch category move is requested.
    /// </summary>
    public event RoutedEventHandler BatchCategoryRequested
    {
        add => AddHandler(BatchCategoryRequestedEvent, value);
        remove => RemoveHandler(BatchCategoryRequestedEvent, value);
    }

    /// <summary>
    /// Routed event raised when the user requests to edit a video's info from the context menu.
    /// Parent controls (e.g. MainWindow) can subscribe to open the EditDialog.
    /// </summary>
    public static readonly RoutedEvent EditVideoRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(EditVideoRequested),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(VideoListView));

    /// <summary>
    /// Occurs when editing a video's info is requested via the context menu.
    /// </summary>
    public event RoutedEventHandler EditVideoRequested
    {
        add => AddHandler(EditVideoRequestedEvent, value);
        remove => RemoveHandler(EditVideoRequestedEvent, value);
    }

    /// <summary>
    /// Routed event raised when the user requests to delete a video from the context menu.
    /// Parent controls (e.g. MainWindow) can subscribe to show the delete confirmation dialog.
    /// </summary>
    public static readonly RoutedEvent DeleteVideoRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(DeleteVideoRequested),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(VideoListView));

    /// <summary>
    /// Occurs when deleting a video is requested via the context menu.
    /// </summary>
    public event RoutedEventHandler DeleteVideoRequested
    {
        add => AddHandler(DeleteVideoRequestedEvent, value);
        remove => RemoveHandler(DeleteVideoRequestedEvent, value);
    }

    public VideoListView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles double-click on a video list item.
    /// Raises the <see cref="VideoDoubleClicked"/> bubbling routed event
    /// so that parent controls can respond (e.g. open the video player).
    /// </summary>
    private void VideoItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is VideoEntry)
        {
            RaiseEvent(new RoutedEventArgs(VideoDoubleClickedEvent, this));
        }
    }

    /// <summary>
    /// Opens the containing folder of the video file in Windows Explorer,
    /// with the file selected.
    /// </summary>
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem &&
            menuItem.DataContext is VideoEntry video &&
            !string.IsNullOrWhiteSpace(video.FilePath) &&
            File.Exists(video.FilePath))
        {
            Process.Start("explorer.exe", $"/select,\"{video.FilePath}\"");
        }
    }

    /// <summary>
    /// Handles the "编辑信息" context menu click.
    /// Sets the right-clicked video as the selected video and raises the
    /// <see cref="EditVideoRequested"/> routed event so that the parent (MainWindow)
    /// can open the EditDialog. (Req 1.2)
    /// </summary>
    private void EditInfo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem &&
            menuItem.DataContext is VideoEntry video &&
            DataContext is VideoListViewModel vm)
        {
            vm.SelectedVideo = video;
            RaiseEvent(new RoutedEventArgs(EditVideoRequestedEvent, this));
        }
    }

    /// <summary>
    /// Handles the "删除视频" context menu click.
    /// Sets the right-clicked video as the selected video and raises the
    /// <see cref="DeleteVideoRequested"/> routed event so that the parent (MainWindow)
    /// can show the delete confirmation dialog. (Req 1.4)
    /// </summary>
    private void DeleteVideo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem &&
            menuItem.DataContext is VideoEntry video &&
            DataContext is VideoListViewModel vm)
        {
            vm.SelectedVideo = video;
            RaiseEvent(new RoutedEventArgs(DeleteVideoRequestedEvent, this));
        }
    }

    /// <summary>
    /// Handles the "复制文件路径" context menu click.
    /// Copies the video's full file path to the system clipboard. (Req 1.3)
    /// </summary>
    private void CopyFilePath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem &&
            menuItem.DataContext is VideoEntry video &&
            !string.IsNullOrWhiteSpace(video.FilePath))
        {
            Clipboard.SetText(video.FilePath);
        }
    }

    /// <summary>
    /// Handles the "使用系统播放器打开" context menu click.
    /// Opens the video file with the OS default associated application. (Req 13.1, 13.2)
    /// Shows an error message if the file does not exist.
    /// </summary>
    private void OpenWithSystemPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem &&
            menuItem.DataContext is VideoEntry video &&
            !string.IsNullOrWhiteSpace(video.FilePath))
        {
            if (!File.Exists(video.FilePath))
            {
                MessageBox.Show(
                    $"文件不存在：{video.FilePath}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = video.FilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"无法打开文件：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Synchronizes the ListBox selection with the ViewModel's SelectedVideos collection.
    /// Supports Ctrl+Click and Shift+Click multi-selection via SelectionMode="Extended".
    /// </summary>
    private void VideoListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not VideoListViewModel vm) return;

        // Add newly selected items
        foreach (var item in e.AddedItems)
        {
            if (item is VideoEntry video && !vm.SelectedVideos.Contains(video))
            {
                vm.SelectedVideos.Add(video);
            }
        }

        // Remove deselected items
        foreach (var item in e.RemovedItems)
        {
            if (item is VideoEntry video)
            {
                vm.SelectedVideos.Remove(video);
            }
        }
    }

    /// <summary>
    /// Raises the BatchDeleteRequested routed event when the batch delete button is clicked.
    /// </summary>
    private void BatchDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(BatchDeleteRequestedEvent, this));
    }

    /// <summary>
    /// Raises the BatchTagRequested routed event when the batch tag button is clicked.
    /// </summary>
    private void BatchTagButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(BatchTagRequestedEvent, this));
    }

    /// <summary>
    /// Raises the BatchCategoryRequested routed event when the batch category button is clicked.
    /// </summary>
    private void BatchCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(BatchCategoryRequestedEvent, this));
    }
}
