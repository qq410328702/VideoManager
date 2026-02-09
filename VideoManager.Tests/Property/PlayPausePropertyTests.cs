using VideoManager.Models;
using VideoManager.ViewModels;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for video player play/pause toggle functionality.
/// Tests Property 4: 播放/暂停状态切换
///
/// **Feature: video-manager-optimization, Property 4: 播放/暂停状态切换**
/// **Validates: Requirements 4.3**
///
/// For any playback state (playing or paused), executing toggle should change
/// to the opposite state: playing becomes paused, paused becomes playing.
/// </summary>
public class PlayPausePropertyTests
{
    /// <summary>
    /// Creates a VideoPlayerViewModel with a loaded video so that TogglePlayPause works.
    /// </summary>
    private static VideoPlayerViewModel CreateViewModelWithVideo()
    {
        var vm = new VideoPlayerViewModel();
        // Set CurrentVideo directly to enable toggle functionality
        // TogglePlayPause checks CurrentVideo != null
        vm.CurrentVideo = new VideoEntry
        {
            Id = 1,
            Title = "Test Video",
            FileName = "test.mp4",
            FilePath = "/test/test.mp4",
            Duration = TimeSpan.FromMinutes(10),
            ImportedAt = DateTime.UtcNow
        };
        vm.Duration = TimeSpan.FromMinutes(10);
        return vm;
    }

    /// <summary>
    /// Generates a boolean indicating the initial playing state.
    /// true = start playing, false = start paused.
    /// </summary>
    private static FsCheck.Arbitrary<bool> InitialStateArb()
    {
        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Elements(true, false));
    }

    /// <summary>
    /// Generates a positive number of toggle operations (1 to 20).
    /// </summary>
    private static FsCheck.Arbitrary<int> ToggleCountArb()
    {
        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Choose(1, 20));
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 4: 播放/暂停状态切换**
    /// **Validates: Requirements 4.3**
    ///
    /// For any initial playback state (playing or paused), executing TogglePlayPause
    /// should flip the state: IsPlaying becomes !IsPlaying, IsPaused becomes !IsPaused.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ToggleShouldFlipPlaybackState()
    {
        return FsCheck.Fluent.Prop.ForAll(InitialStateArb(), startPlaying =>
        {
            var vm = CreateViewModelWithVideo();

            // Set initial state
            if (startPlaying)
            {
                vm.TogglePlayPauseCommand.Execute(null); // Start from default (not playing) -> playing
            }
            // else: default state is not playing, not paused

            // If not starting as playing, we need to ensure we're in a known paused state
            // Default after creation: IsPlaying=false, IsPaused=false
            // After one toggle: IsPlaying=true, IsPaused=false
            // We want to test from both playing and paused states
            if (!startPlaying)
            {
                // Toggle once to get to playing, then toggle again to get to paused
                vm.TogglePlayPauseCommand.Execute(null); // -> playing
                vm.TogglePlayPauseCommand.Execute(null); // -> paused
            }

            // Record state before toggle
            bool wasPlaying = vm.IsPlaying;
            bool wasPaused = vm.IsPaused;

            // Execute toggle
            vm.TogglePlayPauseCommand.Execute(null);

            // Verify state flipped
            bool playingFlipped = vm.IsPlaying == !wasPlaying;
            bool pausedFlipped = vm.IsPaused == !wasPaused;

            return playingFlipped && pausedFlipped;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 4: 播放/暂停状态切换**
    /// **Validates: Requirements 4.3**
    ///
    /// For any number of consecutive toggles, the final state should be deterministic:
    /// after N toggles from a known initial state, IsPlaying should equal (N % 2 != 0)
    /// when starting from not-playing, or (N % 2 == 0) when starting from playing.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property MultipleTogglesShouldAlternateState()
    {
        return FsCheck.Fluent.Prop.ForAll(ToggleCountArb(), toggleCount =>
        {
            var vm = CreateViewModelWithVideo();

            // Start from not-playing state (default)
            // After each toggle, state alternates: not-playing -> playing -> paused -> playing -> ...

            for (int i = 0; i < toggleCount; i++)
            {
                vm.TogglePlayPauseCommand.Execute(null);
            }

            // After odd number of toggles: should be playing
            // After even number of toggles: should not be playing (paused or initial)
            bool expectedPlaying = toggleCount % 2 != 0;

            return vm.IsPlaying == expectedPlaying;
        });
    }
}
