using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for MetricsService timing accuracy.
/// Tests Property 8: 指标计时器准确性
///
/// **Feature: video-manager-optimization-v3, Property 8: 指标计时器准确性**
/// **Validates: Requirements 13.1, 13.2, 13.3**
///
/// For any sequence of recorded timings via MetricsService:
/// - GetAverageTime returns the arithmetic mean of all recorded timings (or the last 100 if more than 100)
/// - GetLastTime returns the last recorded timing
/// - StartTimer records a timing within ±50ms of actual elapsed time
/// </summary>
public class MetricsServicePropertyTests : IDisposable
{
    private MetricsService? _service;

    public void Dispose()
    {
        _service?.Dispose();
    }

    /// <summary>
    /// Generates a random timing scenario as an int array:
    /// [numTimings, seed]
    /// numTimings: 1-150 (to test both under and over the 100-entry limit)
    /// seed: used to deterministically generate timing values
    /// </summary>
    private static FsCheck.Arbitrary<int[]> TimingScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var numTimings = arr.Length > 0 ? (arr[0] % 150) + 1 : 10;  // 1-150
                var seed = arr.Length > 1 ? Math.Abs(arr[1]) + 1 : 1;       // positive seed
                return new int[] { numTimings, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 2));
    }

    /// <summary>
    /// Generates a list of random timing durations in milliseconds from a seed.
    /// Durations are bounded to [1, 5000] ms to represent realistic operation timings.
    /// </summary>
    private static List<double> GenerateTimingMs(int count, int seed)
    {
        var rng = new Random(seed);
        var timings = new List<double>(count);
        for (int i = 0; i < count; i++)
        {
            // Generate durations between 1ms and 5000ms
            timings.Add(rng.Next(1, 5001));
        }
        return timings;
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 8: 指标计时器准确性 — Average Time Correctness**
    /// **Validates: Requirements 13.1, 13.2, 13.3**
    ///
    /// For any sequence of recorded timings, GetAverageTime should return the arithmetic
    /// mean of the retained timings. When more than MaxTimingEntriesPerOperation (100)
    /// timings are recorded, only the last 100 are retained, and the average is computed
    /// over those.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property GetAverageTime_ReturnsArithmeticMeanOfRetainedTimings()
    {
        return FsCheck.Fluent.Prop.ForAll(TimingScenarioArb(), config =>
        {
            int numTimings = config[0];
            int seed = config[1];

            _service?.Dispose();
            _service = new MetricsService(NullLogger<MetricsService>.Instance);

            var timingsMs = GenerateTimingMs(numTimings, seed);
            var operationName = "test_avg_op";

            // Record all timings via the internal RecordTiming method
            foreach (var ms in timingsMs)
            {
                _service.RecordTiming(operationName, TimeSpan.FromMilliseconds(ms));
            }

            // Calculate expected average: only the last MaxTimingEntriesPerOperation entries
            var maxEntries = MetricsService.MaxTimingEntriesPerOperation;
            var retainedTimings = numTimings > maxEntries
                ? timingsMs.Skip(numTimings - maxEntries).ToList()
                : timingsMs;

            var expectedAverageTicks = (long)retainedTimings
                .Select(ms => TimeSpan.FromMilliseconds(ms).Ticks)
                .Average();
            var expectedAverage = TimeSpan.FromTicks(expectedAverageTicks);

            var actualAverage = _service.GetAverageTime(operationName);

            // Allow a tiny tolerance for floating-point rounding in tick calculations
            var diffTicks = Math.Abs(actualAverage.Ticks - expectedAverage.Ticks);
            // Tolerance: 1 tick per entry (rounding from integer division)
            var toleranceTicks = retainedTimings.Count;

            return diffTicks <= toleranceTicks;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 8: 指标计时器准确性 — Last Time Correctness**
    /// **Validates: Requirements 13.1, 13.2, 13.3**
    ///
    /// For any sequence of recorded timings, GetLastTime should always return the
    /// most recently recorded timing value, regardless of how many timings have been recorded.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property GetLastTime_ReturnsLastRecordedTiming()
    {
        return FsCheck.Fluent.Prop.ForAll(TimingScenarioArb(), config =>
        {
            int numTimings = config[0];
            int seed = config[1];

            _service?.Dispose();
            _service = new MetricsService(NullLogger<MetricsService>.Instance);

            var timingsMs = GenerateTimingMs(numTimings, seed);
            var operationName = "test_last_op";

            // Record all timings
            foreach (var ms in timingsMs)
            {
                _service.RecordTiming(operationName, TimeSpan.FromMilliseconds(ms));
            }

            var expectedLast = TimeSpan.FromMilliseconds(timingsMs[^1]);
            var actualLast = _service.GetLastTime(operationName);

            return actualLast == expectedLast;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 8: 指标计时器准确性 — StartTimer Accuracy**
    /// **Validates: Requirements 13.1, 13.2, 13.3**
    ///
    /// For any operation timed via StartTimer, the recorded elapsed time should be
    /// within ±50ms of the actual delay. We use Thread.Sleep with small delays
    /// and verify the recorded timing is within the tolerance.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 20)]
    public FsCheck.Property StartTimer_RecordsTimingWithinToleranceOfActualElapsed()
    {
        // Generate small delays (10-100ms) to keep test runtime reasonable
        var delayArb = FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Choose(10, 100));

        return FsCheck.Fluent.Prop.ForAll(delayArb, delayMs =>
        {
            _service?.Dispose();
            _service = new MetricsService(NullLogger<MetricsService>.Instance);

            var operationName = $"timer_test_{delayMs}";

            using (_service.StartTimer(operationName))
            {
                Thread.Sleep(delayMs);
            }

            var recordedTime = _service.GetLastTime(operationName);
            var toleranceMs = 50.0;

            // The recorded time should be at least the delay (minus tolerance)
            // and not excessively more than the delay (plus tolerance)
            var lowerBound = delayMs - toleranceMs;
            var upperBound = delayMs + toleranceMs;

            return recordedTime.TotalMilliseconds >= lowerBound
                && recordedTime.TotalMilliseconds <= upperBound;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 8: 指标计时器准确性 — Multiple Operations Independence**
    /// **Validates: Requirements 13.1, 13.2, 13.3**
    ///
    /// For any set of distinct operation names with different timing sequences,
    /// each operation's average and last time are computed independently.
    /// Recording timings for one operation does not affect another.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property MultipleOperations_TimingsAreIndependent()
    {
        return FsCheck.Fluent.Prop.ForAll(TimingScenarioArb(), config =>
        {
            int numTimings = config[0];
            int seed = config[1];

            _service?.Dispose();
            _service = new MetricsService(NullLogger<MetricsService>.Instance);

            var rng = new Random(seed);
            var numOps = (rng.Next(2, 6)); // 2-5 distinct operations
            var operationTimings = new Dictionary<string, List<double>>();

            // Generate and record timings for each operation
            for (int opIdx = 0; opIdx < numOps; opIdx++)
            {
                var opName = $"op_{opIdx}";
                var count = (numTimings / numOps) + 1; // distribute timings
                var timingsMs = GenerateTimingMs(count, seed + opIdx);
                operationTimings[opName] = timingsMs;

                foreach (var ms in timingsMs)
                {
                    _service.RecordTiming(opName, TimeSpan.FromMilliseconds(ms));
                }
            }

            // Verify each operation's last time and average independently
            foreach (var (opName, timingsMs) in operationTimings)
            {
                // Verify last time
                var expectedLast = TimeSpan.FromMilliseconds(timingsMs[^1]);
                var actualLast = _service.GetLastTime(opName);
                if (actualLast != expectedLast)
                    return false;

                // Verify average (considering max entries limit)
                var maxEntries = MetricsService.MaxTimingEntriesPerOperation;
                var retained = timingsMs.Count > maxEntries
                    ? timingsMs.Skip(timingsMs.Count - maxEntries).ToList()
                    : timingsMs;

                var expectedAvgTicks = (long)retained
                    .Select(ms => TimeSpan.FromMilliseconds(ms).Ticks)
                    .Average();
                var expectedAvg = TimeSpan.FromTicks(expectedAvgTicks);
                var actualAvg = _service.GetAverageTime(opName);

                var diffTicks = Math.Abs(actualAvg.Ticks - expectedAvg.Ticks);
                if (diffTicks > retained.Count)
                    return false;
            }

            return true;
        });
    }
}
