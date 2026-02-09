using VideoManager.ViewModels;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for video player skip/seek functionality.
/// Tests Property 3: 播放位置跳转与边界限制
///
/// **Feature: video-manager-optimization, Property 3: 播放位置跳转与边界限制**
/// **Validates: Requirements 4.1, 4.2, 4.5**
///
/// For any video playback position and skip seconds (positive or negative),
/// the resulting position should equal clamp(original position + skip seconds, 0, video duration),
/// i.e., result is always in [0, Duration].
/// </summary>
public class SkipPropertyTests
{
    /// <summary>
    /// Generates a positive duration in seconds (1 to 7200, i.e., up to 2 hours).
    /// </summary>
    private static FsCheck.Arbitrary<int> DurationSecondsArb()
    {
        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Choose(1, 7200));
    }

    /// <summary>
    /// Generates a skip amount in seconds (-10000 to 10000) to cover both forward and backward skips,
    /// including values that exceed video boundaries.
    /// </summary>
    private static FsCheck.Arbitrary<int> SkipSecondsArb()
    {
        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Choose(-10000, 10000));
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 3: 播放位置跳转与边界限制**
    /// **Validates: Requirements 4.1, 4.2, 4.5**
    ///
    /// For any duration, starting position within [0, duration], and skip amount,
    /// the resulting position after Skip() should equal clamp(position + skipSeconds, 0, duration)
    /// and always remain within [0, Duration].
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property SkipShouldClampToValidRange()
    {
        return FsCheck.Fluent.Prop.ForAll(
            DurationSecondsArb(),
            SkipSecondsArb(),
            SkipSecondsArb(),
            (durationSec, positionSec, skipSec) =>
            {
                var vm = new VideoPlayerViewModel();
                var duration = TimeSpan.FromSeconds(durationSec);
                vm.Duration = duration;

                // Clamp starting position to [0, duration]
                var clampedPositionSec = Math.Clamp(positionSec, 0, durationSec);
                vm.Position = TimeSpan.FromSeconds(clampedPositionSec);

                // Execute skip
                vm.Skip(skipSec);

                // Expected: clamp(original + skip, 0, duration)
                var expectedSec = Math.Clamp(clampedPositionSec + skipSec, 0, durationSec);
                var expected = TimeSpan.FromSeconds(expectedSec);

                // Verify result equals expected clamped value
                bool positionCorrect = Math.Abs(vm.Position.TotalSeconds - expected.TotalSeconds) < 0.001;

                // Verify result is always within [0, Duration]
                bool withinBounds = vm.Position >= TimeSpan.Zero && vm.Position <= duration;

                return positionCorrect && withinBounds;
            });
    }
}
