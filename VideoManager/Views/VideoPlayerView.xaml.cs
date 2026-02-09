using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VideoManager.ViewModels;

namespace VideoManager.Views;

/// <summary>
/// Code-behind for VideoPlayerView. Bridges the MediaElement (which requires
/// imperative Play/Pause/Stop calls) with the VideoPlayerViewModel's state.
/// Uses a DispatcherTimer to synchronize the MediaElement position back to the ViewModel.
/// </summary>
public partial class VideoPlayerView : UserControl
{
    private readonly DispatcherTimer _positionTimer;
    private bool _isDraggingPosition;
    private bool _isSyncingFromViewModel;

    public VideoPlayerView()
    {
        InitializeComponent();

        // Timer to update ViewModel.Position from MediaElement.Position
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _positionTimer.Tick += PositionTimer_Tick;

        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Subscribes to ViewModel property changes when the DataContext is set or changed.
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is VideoPlayerViewModel oldVm)
        {
            oldVm.PropertyChanged -= ViewModel_PropertyChanged;
        }

        if (e.NewValue is VideoPlayerViewModel newVm)
        {
            newVm.PropertyChanged += ViewModel_PropertyChanged;

            // If the VM already has a video loaded, set the source immediately
            if (newVm.CurrentVideo != null)
            {
                HandleCurrentVideoChanged(newVm);
            }
        }
    }

    /// <summary>
    /// Cleans up resources when the control is unloaded.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _positionTimer.Stop();

        if (DataContext is VideoPlayerViewModel vm)
        {
            vm.PropertyChanged -= ViewModel_PropertyChanged;
        }
    }

    /// <summary>
    /// Responds to ViewModel property changes to control the MediaElement.
    /// MediaElement requires imperative calls for Play(), Pause(), Stop(),
    /// and Source must be set programmatically for Manual loaded behavior.
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not VideoPlayerViewModel vm) return;

        switch (e.PropertyName)
        {
            case nameof(VideoPlayerViewModel.IsPlaying):
                HandlePlaybackStateChanged(vm);
                break;

            case nameof(VideoPlayerViewModel.IsPaused):
                if (vm.IsPaused)
                {
                    VideoMediaElement.Pause();
                    _positionTimer.Stop();
                }
                break;

            case nameof(VideoPlayerViewModel.CurrentVideo):
                HandleCurrentVideoChanged(vm);
                break;

            case nameof(VideoPlayerViewModel.Position):
                HandlePositionChanged(vm);
                break;

            case nameof(VideoPlayerViewModel.PlaybackSpeed):
                HandlePlaybackSpeedChanged(vm);
                break;
        }
    }

    /// <summary>
    /// Synchronizes the ViewModel's PlaybackSpeed to the MediaElement's SpeedRatio.
    /// </summary>
    private void HandlePlaybackSpeedChanged(VideoPlayerViewModel vm)
    {
        VideoMediaElement.SpeedRatio = vm.PlaybackSpeed;
    }

    /// <summary>
    /// Handles changes to the IsPlaying state. Starts or stops the MediaElement
    /// and the position update timer accordingly.
    /// </summary>
    private void HandlePlaybackStateChanged(VideoPlayerViewModel vm)
    {
        if (vm.IsPlaying)
        {
            VideoMediaElement.Play();
            _positionTimer.Start();
        }
        else if (!vm.IsPaused)
        {
            // Stopped state
            VideoMediaElement.Stop();
            _positionTimer.Stop();
        }
    }

    /// <summary>
    /// Handles changes to the CurrentVideo property. Loads the new video source
    /// into the MediaElement or clears it if null.
    /// </summary>
    private void HandleCurrentVideoChanged(VideoPlayerViewModel vm)
    {
        _positionTimer.Stop();

        if (vm.CurrentVideo != null && !string.IsNullOrWhiteSpace(vm.CurrentVideo.FilePath))
        {
            try
            {
                VideoMediaElement.Source = new Uri(vm.CurrentVideo.FilePath, UriKind.Absolute);
            }
            catch (UriFormatException)
            {
                VideoMediaElement.Source = null;
            }
        }
        else
        {
            VideoMediaElement.Source = null;
        }
    }

    /// <summary>
    /// Handles changes to the Position property from the ViewModel (e.g., when the user
    /// drags the position slider). Seeks the MediaElement to the new position.
    /// </summary>
    private void HandlePositionChanged(VideoPlayerViewModel vm)
    {
        // Avoid feedback loop: don't seek if we're the ones updating Position via the timer
        if (_isSyncingFromViewModel) return;

        // Seek the MediaElement whenever the user changes position (drag or click)
        VideoMediaElement.Position = vm.Position;
    }

    /// <summary>
    /// Timer tick handler: reads the current position from the MediaElement
    /// and updates the ViewModel's Position property.
    /// </summary>
    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (_isDraggingPosition) return;

        if (DataContext is VideoPlayerViewModel vm && vm.IsPlaying)
        {
            _isSyncingFromViewModel = true;
            try
            {
                vm.Position = VideoMediaElement.Position;
            }
            finally
            {
                _isSyncingFromViewModel = false;
            }
        }
    }

    /// <summary>
    /// Called when the MediaElement successfully opens a media file.
    /// Updates the ViewModel's Duration from the actual media duration
    /// and synchronizes the playback speed to the MediaElement.
    /// </summary>
    private void VideoMediaElement_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (DataContext is VideoPlayerViewModel vm)
        {
            if (VideoMediaElement.NaturalDuration.HasTimeSpan)
            {
                vm.Duration = VideoMediaElement.NaturalDuration.TimeSpan;
            }

            // Ensure SpeedRatio is synchronized when media opens
            VideoMediaElement.SpeedRatio = vm.PlaybackSpeed;
        }
    }

    /// <summary>
    /// Called when the media reaches the end. Stops playback and resets position.
    /// </summary>
    private void VideoMediaElement_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (DataContext is VideoPlayerViewModel vm)
        {
            vm.StopCommand.Execute(null);
        }
    }

    /// <summary>
    /// Called when the MediaElement encounters an error (e.g., unsupported format,
    /// corrupted file). Sets the error state on the ViewModel.
    /// </summary>
    private void VideoMediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (DataContext is VideoPlayerViewModel vm)
        {
            _positionTimer.Stop();

            var errorMessage = e.ErrorException?.Message ?? "无法播放视频文件";
            vm.HasError = true;
            vm.ErrorMessage = errorMessage;
        }
    }

    /// <summary>
    /// Handles the start of a position slider drag. Pauses position timer updates
    /// to avoid conflicts while the user is seeking.
    /// </summary>
    private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingPosition = true;
    }

    /// <summary>
    /// Handles the end of a position slider drag. Seeks the MediaElement to the
    /// final position and resumes timer updates.
    /// </summary>
    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingPosition = false;

        if (DataContext is VideoPlayerViewModel vm)
        {
            // Seek MediaElement to the position set by the slider
            VideoMediaElement.Position = vm.Position;
        }
    }

    /// <summary>
    /// Handles keyboard shortcuts for video playback control.
    /// Left arrow: skip back 5 seconds. Right arrow: skip forward 5 seconds.
    /// Space: toggle play/pause. S: cycle playback speed.
    /// </summary>
    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not VideoPlayerViewModel vm) return;

        switch (e.Key)
        {
            case Key.Left:
                vm.Skip(-5);
                VideoMediaElement.Position = vm.Position;
                e.Handled = true;
                break;

            case Key.Right:
                vm.Skip(5);
                VideoMediaElement.Position = vm.Position;
                e.Handled = true;
                break;

            case Key.Space:
                vm.TogglePlayPauseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.S:
                vm.CycleSpeedCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
