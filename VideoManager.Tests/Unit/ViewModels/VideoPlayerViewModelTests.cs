using System.IO;
using VideoManager.Models;
using VideoManager.ViewModels;

namespace VideoManager.Tests.Unit.ViewModels;

public class VideoPlayerViewModelTests
{
    private static VideoPlayerViewModel CreateViewModel() => new();

    private static VideoEntry CreateVideoEntry(
        string? filePath = null,
        TimeSpan? duration = null)
    {
        return new VideoEntry
        {
            Id = 1,
            Title = "Test Video",
            FileName = "test.mp4",
            FilePath = filePath ?? "/videos/test.mp4",
            Duration = duration ?? TimeSpan.FromMinutes(5),
            Width = 1920,
            Height = 1080,
            ImportedAt = DateTime.UtcNow
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_InitializesDefaultValues()
    {
        var vm = CreateViewModel();

        Assert.Null(vm.CurrentVideo);
        Assert.False(vm.IsPlaying);
        Assert.False(vm.IsPaused);
        Assert.Equal(1.0, vm.Volume);
        Assert.Equal(TimeSpan.Zero, vm.Position);
        Assert.Equal(TimeSpan.Zero, vm.Duration);
        Assert.Equal(0, vm.PositionPercent);
        Assert.False(vm.HasError);
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }

    #endregion

    #region OpenVideoAsync Tests

    [Fact]
    public async Task OpenVideoAsync_WithNullVideo_ThrowsArgumentNullException()
    {
        var vm = CreateViewModel();
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.OpenVideoAsync(null!));
    }

    [Fact]
    public async Task OpenVideoAsync_SetsCurrentVideoAndDuration()
    {
        var vm = CreateViewModel();
        var video = CreateVideoEntry(filePath: CreateTempVideoFile(), duration: TimeSpan.FromMinutes(10));

        await vm.OpenVideoAsync(video);

        Assert.Equal(video, vm.CurrentVideo);
        Assert.Equal(TimeSpan.FromMinutes(10), vm.Duration);
    }

    [Fact]
    public async Task OpenVideoAsync_ResetsPlaybackState()
    {
        var vm = CreateViewModel();
        var video1 = CreateVideoEntry(filePath: CreateTempVideoFile());

        // Start playing first video
        await vm.OpenVideoAsync(video1);
        vm.PlayCommand.Execute(null);
        Assert.True(vm.IsPlaying);

        // Open a second video - should reset state
        var video2 = CreateVideoEntry(filePath: CreateTempVideoFile(), duration: TimeSpan.FromMinutes(3));
        await vm.OpenVideoAsync(video2);

        Assert.False(vm.IsPlaying);
        Assert.False(vm.IsPaused);
        Assert.Equal(TimeSpan.Zero, vm.Position);
    }

    [Fact]
    public async Task OpenVideoAsync_WithMissingFile_SetsError()
    {
        var vm = CreateViewModel();
        var video = CreateVideoEntry(filePath: "/nonexistent/path/video.mp4");

        await vm.OpenVideoAsync(video);

        Assert.True(vm.HasError);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Equal(video, vm.CurrentVideo);
    }

    [Fact]
    public async Task OpenVideoAsync_WithEmptyFilePath_SetsError()
    {
        var vm = CreateViewModel();
        var video = CreateVideoEntry(filePath: "");

        await vm.OpenVideoAsync(video);

        Assert.True(vm.HasError);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public async Task OpenVideoAsync_WithValidFile_ClearsError()
    {
        var vm = CreateViewModel();

        // First open a missing file to set error
        var badVideo = CreateVideoEntry(filePath: "/nonexistent/video.mp4");
        await vm.OpenVideoAsync(badVideo);
        Assert.True(vm.HasError);

        // Now open a valid file
        var goodVideo = CreateVideoEntry(filePath: CreateTempVideoFile());
        await vm.OpenVideoAsync(goodVideo);

        Assert.False(vm.HasError);
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }

    #endregion

    #region Play Command Tests

    [Fact]
    public async Task PlayCommand_WhenVideoLoaded_SetsIsPlayingTrue()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));

        vm.PlayCommand.Execute(null);

        Assert.True(vm.IsPlaying);
        Assert.False(vm.IsPaused);
    }

    [Fact]
    public void PlayCommand_WhenNoVideoLoaded_CannotExecute()
    {
        var vm = CreateViewModel();
        Assert.False(vm.PlayCommand.CanExecute(null));
    }

    [Fact]
    public async Task PlayCommand_WhenAlreadyPlaying_CannotExecute()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));
        vm.PlayCommand.Execute(null);

        Assert.False(vm.PlayCommand.CanExecute(null));
    }

    [Fact]
    public async Task PlayCommand_AfterPause_ResumesPlayback()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));

        vm.PlayCommand.Execute(null);
        vm.PauseCommand.Execute(null);
        Assert.True(vm.IsPaused);

        // Should be able to play again after pause
        Assert.True(vm.PlayCommand.CanExecute(null));
        vm.PlayCommand.Execute(null);

        Assert.True(vm.IsPlaying);
        Assert.False(vm.IsPaused);
    }

    #endregion

    #region Pause Command Tests

    [Fact]
    public async Task PauseCommand_WhenPlaying_SetsIsPausedTrue()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));
        vm.PlayCommand.Execute(null);

        vm.PauseCommand.Execute(null);

        Assert.False(vm.IsPlaying);
        Assert.True(vm.IsPaused);
    }

    [Fact]
    public void PauseCommand_WhenNotPlaying_CannotExecute()
    {
        var vm = CreateViewModel();
        Assert.False(vm.PauseCommand.CanExecute(null));
    }

    [Fact]
    public async Task PauseCommand_WhenAlreadyPaused_CannotExecute()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));
        vm.PlayCommand.Execute(null);
        vm.PauseCommand.Execute(null);

        Assert.False(vm.PauseCommand.CanExecute(null));
    }

    #endregion

    #region Stop Command Tests

    [Fact]
    public async Task StopCommand_WhenPlaying_StopsAndResetsPosition()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));
        vm.PlayCommand.Execute(null);
        vm.Position = TimeSpan.FromMinutes(2);

        vm.StopCommand.Execute(null);

        Assert.False(vm.IsPlaying);
        Assert.False(vm.IsPaused);
        Assert.Equal(TimeSpan.Zero, vm.Position);
    }

    [Fact]
    public async Task StopCommand_WhenPaused_StopsAndResetsPosition()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));
        vm.PlayCommand.Execute(null);
        vm.Position = TimeSpan.FromMinutes(1);
        vm.PauseCommand.Execute(null);

        vm.StopCommand.Execute(null);

        Assert.False(vm.IsPlaying);
        Assert.False(vm.IsPaused);
        Assert.Equal(TimeSpan.Zero, vm.Position);
    }

    [Fact]
    public void StopCommand_WhenNoVideoLoaded_CannotExecute()
    {
        var vm = CreateViewModel();
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task StopCommand_WhenStopped_CannotExecute()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));

        // Video loaded but not playing or paused
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    #endregion

    #region Volume Tests

    [Fact]
    public void Volume_DefaultIsOne()
    {
        var vm = CreateViewModel();
        Assert.Equal(1.0, vm.Volume);
    }

    [Fact]
    public void SetVolume_ClampsToValidRange()
    {
        var vm = CreateViewModel();

        vm.SetVolume(0.5);
        Assert.Equal(0.5, vm.Volume);

        vm.SetVolume(-0.5);
        Assert.Equal(0.0, vm.Volume);

        vm.SetVolume(1.5);
        Assert.Equal(1.0, vm.Volume);
    }

    [Fact]
    public void SetVolume_SetsExactBoundaryValues()
    {
        var vm = CreateViewModel();

        vm.SetVolume(0.0);
        Assert.Equal(0.0, vm.Volume);

        vm.SetVolume(1.0);
        Assert.Equal(1.0, vm.Volume);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void SetVolume_AcceptsValidValues(double volume)
    {
        var vm = CreateViewModel();
        vm.SetVolume(volume);
        Assert.Equal(volume, vm.Volume);
    }

    #endregion

    #region Position and PositionPercent Tests

    [Fact]
    public void PositionPercent_WhenDurationIsZero_ReturnsZero()
    {
        var vm = CreateViewModel();
        vm.Duration = TimeSpan.Zero;
        vm.Position = TimeSpan.FromMinutes(1);

        Assert.Equal(0, vm.PositionPercent);
    }

    [Fact]
    public async Task PositionPercent_CalculatesCorrectly()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(
            filePath: CreateTempVideoFile(),
            duration: TimeSpan.FromMinutes(10)));

        vm.Position = TimeSpan.FromMinutes(5);

        Assert.Equal(50.0, vm.PositionPercent, precision: 1);
    }

    [Fact]
    public async Task PositionPercent_AtStart_IsZero()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(
            filePath: CreateTempVideoFile(),
            duration: TimeSpan.FromMinutes(10)));

        vm.Position = TimeSpan.Zero;

        Assert.Equal(0.0, vm.PositionPercent);
    }

    [Fact]
    public async Task PositionPercent_AtEnd_IsHundred()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(
            filePath: CreateTempVideoFile(),
            duration: TimeSpan.FromMinutes(10)));

        vm.Position = TimeSpan.FromMinutes(10);

        Assert.Equal(100.0, vm.PositionPercent, precision: 1);
    }

    [Fact]
    public async Task PositionPercent_Set_UpdatesPosition()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(
            filePath: CreateTempVideoFile(),
            duration: TimeSpan.FromSeconds(100)));

        vm.PositionPercent = 50.0;

        Assert.Equal(TimeSpan.FromSeconds(50), vm.Position);
    }

    [Fact]
    public async Task PositionPercent_Set_ClampsToValidRange()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(
            filePath: CreateTempVideoFile(),
            duration: TimeSpan.FromSeconds(100)));

        vm.PositionPercent = 150.0;
        Assert.Equal(100.0, vm.PositionPercent, precision: 1);

        vm.PositionPercent = -50.0;
        Assert.Equal(0.0, vm.PositionPercent, precision: 1);
    }

    [Fact]
    public void PositionPercent_Set_WhenDurationIsZero_DoesNothing()
    {
        var vm = CreateViewModel();
        vm.Duration = TimeSpan.Zero;

        vm.PositionPercent = 50.0;

        Assert.Equal(TimeSpan.Zero, vm.Position);
    }

    #endregion

    #region PropertyChanged Notification Tests

    [Fact]
    public async Task IsPlaying_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));

        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsPlaying)) raised = true;
        };

        vm.PlayCommand.Execute(null);
        Assert.True(raised);
    }

    [Fact]
    public async Task Position_RaisesPositionPercentChanged()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(
            filePath: CreateTempVideoFile(),
            duration: TimeSpan.FromMinutes(10)));

        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.PositionPercent)) raised = true;
        };

        vm.Position = TimeSpan.FromMinutes(5);
        Assert.True(raised);
    }

    [Fact]
    public void Volume_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();

        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Volume)) raised = true;
        };

        vm.SetVolume(0.5);
        Assert.True(raised);
    }

    #endregion

    #region Playback State Transitions

    [Fact]
    public async Task FullPlaybackCycle_Play_Pause_Resume_Stop()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));

        // Initial state: stopped
        Assert.False(vm.IsPlaying);
        Assert.False(vm.IsPaused);

        // Play
        vm.PlayCommand.Execute(null);
        Assert.True(vm.IsPlaying);
        Assert.False(vm.IsPaused);

        // Pause
        vm.PauseCommand.Execute(null);
        Assert.False(vm.IsPlaying);
        Assert.True(vm.IsPaused);

        // Resume (play again)
        vm.PlayCommand.Execute(null);
        Assert.True(vm.IsPlaying);
        Assert.False(vm.IsPaused);

        // Stop
        vm.StopCommand.Execute(null);
        Assert.False(vm.IsPlaying);
        Assert.False(vm.IsPaused);
        Assert.Equal(TimeSpan.Zero, vm.Position);
    }

    [Fact]
    public async Task OpenNewVideo_WhilePlaying_ResetsState()
    {
        var vm = CreateViewModel();
        var video1 = CreateVideoEntry(filePath: CreateTempVideoFile(), duration: TimeSpan.FromMinutes(5));
        var video2 = CreateVideoEntry(filePath: CreateTempVideoFile(), duration: TimeSpan.FromMinutes(10));

        await vm.OpenVideoAsync(video1);
        vm.PlayCommand.Execute(null);
        vm.Position = TimeSpan.FromMinutes(2);

        await vm.OpenVideoAsync(video2);

        Assert.Equal(video2, vm.CurrentVideo);
        Assert.Equal(TimeSpan.FromMinutes(10), vm.Duration);
        Assert.False(vm.IsPlaying);
        Assert.False(vm.IsPaused);
        Assert.Equal(TimeSpan.Zero, vm.Position);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task OpenVideoAsync_WithNullFilePath_SetsError()
    {
        var vm = CreateViewModel();
        var video = new VideoEntry
        {
            Id = 1,
            Title = "Test",
            FileName = "test.mp4",
            FilePath = null!,
            Duration = TimeSpan.FromMinutes(5)
        };

        await vm.OpenVideoAsync(video);

        Assert.True(vm.HasError);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    #endregion

    #region CycleSpeed Tests

    [Fact]
    public void CycleSpeed_DefaultSpeedIs1x()
    {
        var vm = CreateViewModel();
        Assert.Equal(1.0, vm.PlaybackSpeed);
    }

    [Fact]
    public void SpeedOptions_ContainsExpectedValues()
    {
        Assert.Equal(new[] { 0.5, 1.0, 1.5, 2.0 }, VideoPlayerViewModel.SpeedOptions);
    }

    [Fact]
    public void CycleSpeed_FromDefault_AdvancesTo1_5x()
    {
        var vm = CreateViewModel();

        vm.CycleSpeedCommand.Execute(null);

        Assert.Equal(1.5, vm.PlaybackSpeed);
    }

    [Fact]
    public void CycleSpeed_CyclesThroughAllSpeeds()
    {
        var vm = CreateViewModel();

        // Default is 1.0x (index 1)
        vm.CycleSpeedCommand.Execute(null); // → 1.5x
        Assert.Equal(1.5, vm.PlaybackSpeed);

        vm.CycleSpeedCommand.Execute(null); // → 2.0x
        Assert.Equal(2.0, vm.PlaybackSpeed);

        vm.CycleSpeedCommand.Execute(null); // → 0.5x (wraps around)
        Assert.Equal(0.5, vm.PlaybackSpeed);

        vm.CycleSpeedCommand.Execute(null); // → 1.0x
        Assert.Equal(1.0, vm.PlaybackSpeed);
    }

    [Fact]
    public void CycleSpeed_WrapsAroundAfterMaxSpeed()
    {
        var vm = CreateViewModel();

        // Cycle 4 times to get back to start
        for (int i = 0; i < 4; i++)
            vm.CycleSpeedCommand.Execute(null);

        Assert.Equal(1.0, vm.PlaybackSpeed);
    }

    [Fact]
    public void CycleSpeed_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();

        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.PlaybackSpeed)) raised = true;
        };

        vm.CycleSpeedCommand.Execute(null);
        Assert.True(raised);
    }

    #endregion

    #region Skip Tests

    [Fact]
    public void Skip_Forward_AdvancesPosition()
    {
        var vm = CreateViewModel();
        vm.Duration = TimeSpan.FromMinutes(10);
        vm.Position = TimeSpan.FromMinutes(2);

        vm.Skip(5);

        Assert.Equal(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(5), vm.Position);
    }

    [Fact]
    public void Skip_Backward_RewindsPosition()
    {
        var vm = CreateViewModel();
        vm.Duration = TimeSpan.FromMinutes(10);
        vm.Position = TimeSpan.FromMinutes(2);

        vm.Skip(-5);

        Assert.Equal(TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(5), vm.Position);
    }

    [Fact]
    public void Skip_Forward_ClampsToEndOfVideo()
    {
        var vm = CreateViewModel();
        vm.Duration = TimeSpan.FromSeconds(100);
        vm.Position = TimeSpan.FromSeconds(98);

        vm.Skip(10); // Would go to 108, but should clamp to 100

        Assert.Equal(TimeSpan.FromSeconds(100), vm.Position);
    }

    [Fact]
    public void Skip_Backward_ClampsToStartOfVideo()
    {
        var vm = CreateViewModel();
        vm.Duration = TimeSpan.FromSeconds(100);
        vm.Position = TimeSpan.FromSeconds(3);

        vm.Skip(-10); // Would go to -7, but should clamp to 0

        Assert.Equal(TimeSpan.Zero, vm.Position);
    }

    [Fact]
    public void Skip_FromZero_Backward_StaysAtZero()
    {
        var vm = CreateViewModel();
        vm.Duration = TimeSpan.FromMinutes(10);
        vm.Position = TimeSpan.Zero;

        vm.Skip(-5);

        Assert.Equal(TimeSpan.Zero, vm.Position);
    }

    [Fact]
    public void Skip_FromEnd_Forward_StaysAtEnd()
    {
        var vm = CreateViewModel();
        vm.Duration = TimeSpan.FromMinutes(10);
        vm.Position = TimeSpan.FromMinutes(10);

        vm.Skip(5);

        Assert.Equal(TimeSpan.FromMinutes(10), vm.Position);
    }

    [Fact]
    public void Skip_ZeroSeconds_PositionUnchanged()
    {
        var vm = CreateViewModel();
        vm.Duration = TimeSpan.FromMinutes(10);
        vm.Position = TimeSpan.FromMinutes(5);

        vm.Skip(0);

        Assert.Equal(TimeSpan.FromMinutes(5), vm.Position);
    }

    [Fact]
    public void Skip_WithZeroDuration_ClampsToZero()
    {
        var vm = CreateViewModel();
        vm.Duration = TimeSpan.Zero;
        vm.Position = TimeSpan.Zero;

        vm.Skip(5);

        Assert.Equal(TimeSpan.Zero, vm.Position);
    }

    #endregion

    #region TogglePlayPause Tests

    [Fact]
    public async Task TogglePlayPause_WhenStopped_StartsPlaying()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));

        vm.TogglePlayPauseCommand.Execute(null);

        Assert.True(vm.IsPlaying);
        Assert.False(vm.IsPaused);
    }

    [Fact]
    public async Task TogglePlayPause_WhenPlaying_Pauses()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));
        vm.PlayCommand.Execute(null);

        vm.TogglePlayPauseCommand.Execute(null);

        Assert.False(vm.IsPlaying);
        Assert.True(vm.IsPaused);
    }

    [Fact]
    public async Task TogglePlayPause_WhenPaused_Resumes()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));
        vm.PlayCommand.Execute(null);
        vm.PauseCommand.Execute(null);

        vm.TogglePlayPauseCommand.Execute(null);

        Assert.True(vm.IsPlaying);
        Assert.False(vm.IsPaused);
    }

    [Fact]
    public void TogglePlayPause_WithNoVideo_DoesNothing()
    {
        var vm = CreateViewModel();

        vm.TogglePlayPauseCommand.Execute(null);

        Assert.False(vm.IsPlaying);
        Assert.False(vm.IsPaused);
    }

    [Fact]
    public async Task TogglePlayPause_DoubleTap_ReturnsToOriginalState()
    {
        var vm = CreateViewModel();
        await vm.OpenVideoAsync(CreateVideoEntry(filePath: CreateTempVideoFile()));
        vm.PlayCommand.Execute(null);

        // Toggle twice should return to playing
        vm.TogglePlayPauseCommand.Execute(null); // pause
        vm.TogglePlayPauseCommand.Execute(null); // play

        Assert.True(vm.IsPlaying);
        Assert.False(vm.IsPaused);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a temporary file to simulate a video file existing on disk.
    /// </summary>
    private static string CreateTempVideoFile()
    {
        var tempPath = Path.GetTempFileName();
        // Rename to .mp4 extension
        var mp4Path = Path.ChangeExtension(tempPath, ".mp4");
        if (File.Exists(mp4Path)) File.Delete(mp4Path);
        File.Move(tempPath, mp4Path);
        return mp4Path;
    }

    #endregion
}
