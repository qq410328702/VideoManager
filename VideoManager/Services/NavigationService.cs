using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VideoManager.Models;
using VideoManager.ViewModels;
using VideoManager.Views;

namespace VideoManager.Services;

/// <summary>
/// Implements <see cref="INavigationService"/> by creating WPF windows on the UI thread.
/// Uses <see cref="Application.Current.Dispatcher"/> to ensure all window operations
/// happen on the UI thread, and resolves ViewModels through the DI container.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Creates a new NavigationService.
    /// </summary>
    /// <param name="serviceProvider">The DI service provider for resolving ViewModels.</param>
    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public async Task OpenVideoPlayerAsync(VideoEntry video)
    {
        ArgumentNullException.ThrowIfNull(video);

        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var playerVm = _serviceProvider.GetRequiredService<VideoPlayerViewModel>();
            var playerView = new VideoPlayerView { DataContext = playerVm };

            var playerWindow = new Window
            {
                Title = $"播放 - {video.Title}",
                Content = playerView,
                Width = 900,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow
            };
            playerWindow.Closed += (_, _) => playerVm.StopCommand.Execute(null);

            // Open video AFTER DataContext is set so the View receives PropertyChanged
            await playerVm.OpenVideoAsync(video);

            playerWindow.ShowDialog();
        });
    }

    /// <inheritdoc />
    public async Task<ImportResult?> OpenImportDialogAsync()
    {
        ImportResult? result = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var importVm = _serviceProvider.GetRequiredService<ImportViewModel>();
            var dialog = new ImportDialog(importVm)
            {
                Owner = Application.Current.MainWindow
            };

            dialog.ShowDialog();

            if (importVm.ImportResult != null && importVm.ImportResult.SuccessCount > 0)
            {
                result = importVm.ImportResult;
            }
        });

        return result;
    }

    /// <inheritdoc />
    public async Task OpenDiagnosticsAsync()
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var diagnosticsVm = _serviceProvider.GetRequiredService<DiagnosticsViewModel>();
            var diagnosticsView = new DiagnosticsView { DataContext = diagnosticsVm };

            var diagnosticsWindow = new Window
            {
                Title = "诊断统计",
                Content = diagnosticsView,
                Width = 550,
                Height = 650,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                ResizeMode = ResizeMode.CanResize
            };

            diagnosticsWindow.Closed += (_, _) => diagnosticsVm.Dispose();

            diagnosticsWindow.ShowDialog();
        });
    }
}
