using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using VideoManager.Services;
using VideoManager.ViewModels;

namespace VideoManager;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// Retains only window lifecycle management (size/position persistence),
/// DataContext binding, and keyboard shortcut handling.
/// All business logic is delegated to MainViewModel via commands (Req 11.3, 12.6).
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

        // Wire video double-click to MainViewModel command
        VideoListControl.VideoDoubleClicked += VideoListControl_VideoDoubleClicked;

        // Wire batch operation events to MainViewModel commands
        VideoListControl.BatchDeleteRequested += VideoListControl_BatchDeleteRequested;
        VideoListControl.BatchTagRequested += VideoListControl_BatchTagRequested;
        VideoListControl.BatchCategoryRequested += VideoListControl_BatchCategoryRequested;

        // Wire context menu events to MainViewModel commands
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

    /// <summary>
    /// Keyboard shortcut: Enter key triggers search command.
    /// </summary>
    private void SearchKeywordTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (_mainVm.SearchCommand.CanExecute(null))
                _mainVm.SearchCommand.Execute(null);
        }
    }

    // ==================== Event handlers that delegate to MainViewModel commands ====================

    private async void VideoListControl_VideoDoubleClicked(object sender, RoutedEventArgs e)
    {
        if (_mainVm.OpenVideoPlayerCommand.CanExecute(null))
            await _mainVm.OpenVideoPlayerCommand.ExecuteAsync(null);
    }

    private async void VideoListControl_EditVideoRequested(object sender, RoutedEventArgs e)
    {
        if (_mainVm.EditVideoCommand.CanExecute(null))
            await _mainVm.EditVideoCommand.ExecuteAsync(null);
    }

    private async void VideoListControl_DeleteVideoRequested(object sender, RoutedEventArgs e)
    {
        if (_mainVm.DeleteVideoCommand.CanExecute(null))
            await _mainVm.DeleteVideoCommand.ExecuteAsync(null);
    }

    private async void VideoListControl_BatchDeleteRequested(object sender, RoutedEventArgs e)
    {
        if (_mainVm.BatchDeleteCommand.CanExecute(null))
            await _mainVm.BatchDeleteCommand.ExecuteAsync(null);
    }

    private async void VideoListControl_BatchTagRequested(object sender, RoutedEventArgs e)
    {
        if (_mainVm.BatchTagCommand.CanExecute(null))
            await _mainVm.BatchTagCommand.ExecuteAsync(null);
    }

    private async void VideoListControl_BatchCategoryRequested(object sender, RoutedEventArgs e)
    {
        if (_mainVm.BatchCategoryCommand.CanExecute(null))
            await _mainVm.BatchCategoryCommand.ExecuteAsync(null);
    }
}
