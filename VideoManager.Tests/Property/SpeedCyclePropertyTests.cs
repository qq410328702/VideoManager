using VideoManager.ViewModels;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for video player speed cycle functionality.
/// Tests Property 5: 播放速度循环切换
///
/// **Feature: video-manager-optimization, Property 5: 播放速度循环切换**
/// **Validates: Requirements 4.4**
///
/// For any current speed index i (0-3 corresponding to 0.5x/1.0x/1.5x/2.0x),
/// executing one cycle should change the speed index to (i + 1) % 4,
/// and the speed value should be SpeedOptions[(i + 1) % 4].
/// </summary>
public class SpeedCyclePropertyTests
{
    /// <summary>
    /// Generates a starting speed index (0-3).
    /// </summary>
    private static FsCheck.Arbitrary<int> SpeedIndexArb()
    {
        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Choose(0, 3));
    }

    /// <summary>
    /// Generates a number of cycle operations (1 to 20).
    /// </summary>
    private static FsCheck.Arbitrary<int> CycleCountArb()
    {
        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Choose(1, 20));
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 5: 播放速度循环切换**
    /// **Validates: Requirements 4.4**
    ///
    /// For any starting speed index i (0-3), executing one CycleSpeed should change
    /// the speed to SpeedOptions[(i + 1) % 4].
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property SingleCycleShouldAdvanceToNextSpeed()
    {
        return FsCheck.Fluent.Prop.ForAll(SpeedIndexArb(), startIndex =>
        {
            var vm = new VideoPlayerViewModel();

            // The default _currentSpeedIndex is 1 (1.0x).
            // To set the starting index, we cycle from default index 1 to the desired startIndex.
            // We need to cycle (startIndex - 1 + 4) % 4 times from default index 1 to reach startIndex.
            int cyclesToReachStart = (startIndex - 1 + 4) % 4;
            for (int i = 0; i < cyclesToReachStart; i++)
            {
                vm.CycleSpeedCommand.Execute(null);
            }

            // Verify we're at the expected starting speed
            double expectedStartSpeed = VideoPlayerViewModel.SpeedOptions[startIndex];
            if (Math.Abs(vm.PlaybackSpeed - expectedStartSpeed) > 0.001)
                return false;

            // Execute one cycle
            vm.CycleSpeedCommand.Execute(null);

            // Expected: speed should be SpeedOptions[(startIndex + 1) % 4]
            int expectedIndex = (startIndex + 1) % VideoPlayerViewModel.SpeedOptions.Length;
            double expectedSpeed = VideoPlayerViewModel.SpeedOptions[expectedIndex];

            return Math.Abs(vm.PlaybackSpeed - expectedSpeed) < 0.001;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 5: 播放速度循环切换**
    /// **Validates: Requirements 4.4**
    ///
    /// For any starting speed index and any number of cycles N, the resulting speed
    /// should be SpeedOptions[(startIndex + N) % 4], demonstrating correct cyclic behavior.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property MultipleCyclesShouldWrapAroundCorrectly()
    {
        return FsCheck.Fluent.Prop.ForAll(SpeedIndexArb(), CycleCountArb(), (startIndex, cycleCount) =>
        {
            var vm = new VideoPlayerViewModel();

            // Navigate to starting index from default (index 1)
            int cyclesToReachStart = (startIndex - 1 + 4) % 4;
            for (int i = 0; i < cyclesToReachStart; i++)
            {
                vm.CycleSpeedCommand.Execute(null);
            }

            // Execute N cycles
            for (int i = 0; i < cycleCount; i++)
            {
                vm.CycleSpeedCommand.Execute(null);
            }

            // Expected: SpeedOptions[(startIndex + cycleCount) % 4]
            int expectedIndex = (startIndex + cycleCount) % VideoPlayerViewModel.SpeedOptions.Length;
            double expectedSpeed = VideoPlayerViewModel.SpeedOptions[expectedIndex];

            return Math.Abs(vm.PlaybackSpeed - expectedSpeed) < 0.001;
        });
    }
}
