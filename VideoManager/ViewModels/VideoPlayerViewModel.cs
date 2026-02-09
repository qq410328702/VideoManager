using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoManager.Models;

namespace VideoManager.ViewModels;

/// <summary>
/// ViewModel for the video player view. Manages playback state, volume,
/// position tracking, and playback control commands (play, pause, stop).
/// The actual MediaElement control lives in the View layer; this ViewModel
/// only manages state and exposes commands for data binding.
/// </summary>
public partial class VideoPlayerViewModel : ViewModelBase
{
    /// <summary>
    /// Available playback speed options.
    /// </summary>
    public static readonly double[] SpeedOptions = { 0.5, 1.0, 1.5, 2.0 };

    /// <summary>
    /// Current playback speed (default 1.0x).
    /// </summary>
    [ObservableProperty]
    private double _playbackSpeed = 1.0;

    /// <summary>
    /// Index into SpeedOptions for the current speed.
    /// </summary>
    private int _currentSpeedIndex = 1; // default 1.0x

    /// <summary>
    /// The video entry currently loaded for playback.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private VideoEntry? _currentVideo;

    /// <summary>
    /// Whether the video is currently playing.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isPlaying;

    /// <summary>
    /// Whether the video is currently paused.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    private bool _isPaused;

    /// <summary>
    /// Volume level from 0.0 (mute) to 1.0 (max).
    /// </summary>
    [ObservableProperty]
    private double _volume = 1.0;

    /// <summary>
    /// Current playback position.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionPercent))]
    private TimeSpan _position;

    /// <summary>
    /// Total duration of the currently loaded video.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionPercent))]
    private TimeSpan _duration;

    /// <summary>
    /// Whether there is an error with the current video (e.g., file not found, unsupported format).
    /// </summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// Error message describing why the video cannot be played.
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Current playback position as a percentage (0-100) for slider binding.
    /// Setting this value updates the Position based on the current Duration.
    /// </summary>
    public double PositionPercent
    {
        get
        {
            if (Duration.TotalSeconds <= 0) return 0;
            return Position.TotalSeconds / Duration.TotalSeconds * 100.0;
        }
        set
        {
            if (Duration.TotalSeconds <= 0) return;

            var clampedValue = Math.Clamp(value, 0, 100);
            var newPosition = TimeSpan.FromSeconds(Duration.TotalSeconds * clampedValue / 100.0);

            if (Position != newPosition)
            {
                Position = newPosition;
                // PositionPercent notification is handled by [NotifyPropertyChangedFor] on Position
            }
        }
    }

    /// <summary>
    /// Loads a video entry for playback. Sets the CurrentVideo, Duration,
    /// and resets playback state. Validates that the video file exists.
    /// </summary>
    /// <param name="video">The video entry to load.</param>
    public Task OpenVideoAsync(VideoEntry video)
    {
        ArgumentNullException.ThrowIfNull(video);

        // Reset playback state
        Stop();

        // Validate file exists
        if (string.IsNullOrWhiteSpace(video.FilePath) || !System.IO.File.Exists(video.FilePath))
        {
            CurrentVideo = video;
            Duration = video.Duration;
            SetError("视频文件不存在或路径无效");
            return Task.CompletedTask;
        }

        ClearError();
        CurrentVideo = video;
        Duration = video.Duration;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts or resumes playback of the current video.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play()
    {
        if (CurrentVideo == null) return;

        IsPlaying = true;
        IsPaused = false;
    }

    private bool CanPlay() => CurrentVideo != null && !IsPlaying;

    /// <summary>
    /// Pauses playback of the current video.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        if (!IsPlaying) return;

        IsPlaying = false;
        IsPaused = true;
    }

    private bool CanPause() => CurrentVideo != null && IsPlaying && !IsPaused;

    /// <summary>
    /// Stops playback and resets position to the beginning.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        IsPlaying = false;
        IsPaused = false;
        Position = TimeSpan.Zero;
    }

    private bool CanStop() => CurrentVideo != null && (IsPlaying || IsPaused);

    /// <summary>
    /// Sets the volume level, clamping to the valid range [0.0, 1.0].
    /// </summary>
    /// <param name="volume">The desired volume level.</param>
    public void SetVolume(double volume)
    {
        Volume = Math.Clamp(volume, 0.0, 1.0);
    }

    /// <summary>
    /// Sets an error message and marks HasError as true.
    /// </summary>
    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    /// <summary>
    /// Clears any existing error state.
    /// </summary>
    private void ClearError()
    {
        ErrorMessage = string.Empty;
        HasError = false;
    }

    /// <summary>
    /// Cycles through playback speed options: 0.5x → 1.0x → 1.5x → 2.0x → 0.5x → ...
    /// </summary>
    [RelayCommand]
    private void CycleSpeed()
    {
        _currentSpeedIndex = (_currentSpeedIndex + 1) % SpeedOptions.Length;
        PlaybackSpeed = SpeedOptions[_currentSpeedIndex];
    }

    /// <summary>
    /// Skips playback by the specified number of seconds (positive = forward, negative = backward).
    /// The resulting position is clamped to [0, Duration].
    /// </summary>
    /// <param name="seconds">Number of seconds to skip. Positive for forward, negative for backward.</param>
    public void Skip(double seconds)
    {
        var newPosition = Position + TimeSpan.FromSeconds(seconds);
        if (newPosition < TimeSpan.Zero)
            newPosition = TimeSpan.Zero;
        if (newPosition > Duration)
            newPosition = Duration;
        Position = newPosition;
    }

    /// <summary>
    /// Toggles between play and pause states.
    /// If playing, pauses. If paused or stopped (with a video loaded), plays.
    /// </summary>
    [RelayCommand]
    private void TogglePlayPause()
    {
        if (CurrentVideo == null) return;

        if (IsPlaying)
        {
            IsPlaying = false;
            IsPaused = true;
        }
        else
        {
            IsPlaying = true;
            IsPaused = false;
        }
    }
}
