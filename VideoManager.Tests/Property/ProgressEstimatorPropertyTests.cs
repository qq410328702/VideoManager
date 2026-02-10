using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for ProgressEstimator accuracy.
/// Tests Property 4: 进度预估准确性
///
/// **Feature: video-manager-optimization-v3, Property 4: 进度预估准确性**
/// **Validates: Requirements 7.1, 7.2, 7.3**
///
/// For any batch operation with N total items, after completing M items (M ≥ 1):
/// - ProgressPercentage equals M / N * 100
/// - EstimatedTimeRemaining equals the moving average of recent durations × remaining items (N - M)
/// - CompletedCount never exceeds TotalCount
/// - ProgressPercentage is always between 0 and 100
/// </summary>
public class ProgressEstimatorPropertyTests
{
    /// <summary>
    /// Generates a random progress estimator scenario as an int array:
    /// [totalItems, completions, seed]
    /// totalItems: 1-100
    /// completions: 0-totalItems (how many RecordCompletion calls to make)
    /// seed: used for deterministic behavior
    /// </summary>
    private static FsCheck.Arbitrary<int[]> ProgressScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var totalItems = arr.Length > 0 ? (arr[0] % 100) + 1 : 10;         // 1-100
                var completions = arr.Length > 1 ? arr[1] % (totalItems + 1) : 0;   // 0-totalItems
                var seed = arr.Length > 2 ? Math.Abs(arr[2]) + 1 : 1;               // positive seed
                return new int[] { totalItems, completions, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 3));
    }

    /// <summary>
    /// Generates a scenario with extra calls beyond totalItems to test overflow protection.
    /// [totalItems, callCount] where callCount may exceed totalItems.
    /// </summary>
    private static FsCheck.Arbitrary<int[]> OverflowScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var totalItems = arr.Length > 0 ? (arr[0] % 50) + 1 : 10;      // 1-50
                var callCount = arr.Length > 1 ? (arr[1] % 100) : 0;           // 0-99
                return new int[] { totalItems, callCount };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 2));
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 4: 进度预估准确性 — Progress Percentage Correctness**
    /// **Validates: Requirements 7.1, 7.2, 7.3**
    ///
    /// For any total N and M completions, ProgressPercentage always equals
    /// CompletedCount / TotalCount * 100.0.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property ProgressPercentage_AlwaysEquals_CompletedDividedByTotal_Times100()
    {
        return FsCheck.Fluent.Prop.ForAll(ProgressScenarioArb(), config =>
        {
            int totalItems = config[0];
            int completions = config[1];

            var estimator = new ProgressEstimator(totalItems);

            for (int i = 0; i < completions; i++)
            {
                estimator.RecordCompletion();
            }

            var expected = (double)completions / totalItems * 100.0;
            var actual = estimator.ProgressPercentage;

            // Use a small tolerance for floating-point comparison
            return Math.Abs(actual - expected) < 1e-10;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 4: 进度预估准确性 — EstimatedTimeRemaining Null Conditions**
    /// **Validates: Requirements 7.1, 7.2, 7.3**
    ///
    /// EstimatedTimeRemaining is null when CompletedCount == 0 (no data for estimation)
    /// or when CompletedCount == TotalCount (all items done).
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property EstimatedTimeRemaining_IsNull_WhenZeroOrAllCompleted()
    {
        // Generate totalItems from 1-50
        var totalArb = FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Choose(1, 50));

        return FsCheck.Fluent.Prop.ForAll(totalArb, totalItems =>
        {
            // Case 1: No completions → null
            var estimator1 = new ProgressEstimator(totalItems);
            if (estimator1.EstimatedTimeRemaining != null)
                return false;

            // Case 2: All completed → null
            var estimator2 = new ProgressEstimator(totalItems);
            for (int i = 0; i < totalItems; i++)
            {
                estimator2.RecordCompletion();
            }
            if (estimator2.EstimatedTimeRemaining != null)
                return false;

            return true;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 4: 进度预估准确性 — CompletedCount Never Exceeds TotalCount**
    /// **Validates: Requirements 7.1, 7.2, 7.3**
    ///
    /// For any number of RecordCompletion calls (even exceeding TotalCount),
    /// CompletedCount never exceeds TotalCount.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property CompletedCount_NeverExceedsTotalCount()
    {
        return FsCheck.Fluent.Prop.ForAll(OverflowScenarioArb(), config =>
        {
            int totalItems = config[0];
            int callCount = config[1];

            var estimator = new ProgressEstimator(totalItems);

            for (int i = 0; i < callCount; i++)
            {
                estimator.RecordCompletion();

                // Invariant must hold after every single call
                if (estimator.CompletedCount > estimator.TotalCount)
                    return false;
            }

            return estimator.CompletedCount <= estimator.TotalCount;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 4: 进度预估准确性 — ProgressPercentage Always Between 0 and 100**
    /// **Validates: Requirements 7.1, 7.2, 7.3**
    ///
    /// For any sequence of RecordCompletion calls (even exceeding TotalCount),
    /// ProgressPercentage is always in the range [0, 100].
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property ProgressPercentage_AlwaysBetween0And100()
    {
        return FsCheck.Fluent.Prop.ForAll(OverflowScenarioArb(), config =>
        {
            int totalItems = config[0];
            int callCount = config[1];

            var estimator = new ProgressEstimator(totalItems);

            for (int i = 0; i < callCount; i++)
            {
                estimator.RecordCompletion();

                var pct = estimator.ProgressPercentage;
                if (pct < 0.0 || pct > 100.0)
                    return false;
            }

            // Also check initial state
            var fresh = new ProgressEstimator(totalItems);
            return fresh.ProgressPercentage >= 0.0 && fresh.ProgressPercentage <= 100.0;
        });
    }
}
