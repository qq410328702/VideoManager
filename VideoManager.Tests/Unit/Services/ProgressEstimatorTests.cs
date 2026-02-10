using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="ProgressEstimator"/>.
/// Tests specific examples and edge cases for progress estimation.
/// </summary>
public class ProgressEstimatorTests
{
    [Fact]
    public void Constructor_WithZeroTotalItems_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProgressEstimator(0));
    }

    [Fact]
    public void Constructor_WithNegativeTotalItems_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProgressEstimator(-1));
    }

    [Fact]
    public void Constructor_SetsTotalCount()
    {
        var estimator = new ProgressEstimator(10);
        Assert.Equal(10, estimator.TotalCount);
    }

    [Fact]
    public void NewEstimator_HasZeroCompletedCount()
    {
        var estimator = new ProgressEstimator(5);
        Assert.Equal(0, estimator.CompletedCount);
    }

    [Fact]
    public void NewEstimator_HasZeroProgressPercentage()
    {
        var estimator = new ProgressEstimator(5);
        Assert.Equal(0.0, estimator.ProgressPercentage);
    }

    [Fact]
    public void NewEstimator_EstimatedTimeRemaining_IsNull()
    {
        var estimator = new ProgressEstimator(5);
        Assert.Null(estimator.EstimatedTimeRemaining);
    }

    [Fact]
    public void RecordCompletion_IncrementsCompletedCount()
    {
        var estimator = new ProgressEstimator(5);

        estimator.RecordCompletion();

        Assert.Equal(1, estimator.CompletedCount);
    }

    [Fact]
    public void RecordCompletion_UpdatesProgressPercentage()
    {
        var estimator = new ProgressEstimator(4);

        estimator.RecordCompletion();
        Assert.Equal(25.0, estimator.ProgressPercentage);

        estimator.RecordCompletion();
        Assert.Equal(50.0, estimator.ProgressPercentage);
    }

    [Fact]
    public void ProgressPercentage_AtCompletion_Is100()
    {
        var estimator = new ProgressEstimator(3);

        estimator.RecordCompletion();
        estimator.RecordCompletion();
        estimator.RecordCompletion();

        Assert.Equal(100.0, estimator.ProgressPercentage);
    }

    [Fact]
    public void RecordCompletion_BeyondTotal_DoesNotExceedTotal()
    {
        var estimator = new ProgressEstimator(2);

        estimator.RecordCompletion();
        estimator.RecordCompletion();
        estimator.RecordCompletion(); // Extra call beyond total

        Assert.Equal(2, estimator.CompletedCount);
        Assert.Equal(100.0, estimator.ProgressPercentage);
    }

    [Fact]
    public void EstimatedTimeRemaining_AfterAllCompleted_IsNull()
    {
        var estimator = new ProgressEstimator(1);

        estimator.RecordCompletion();

        Assert.Null(estimator.EstimatedTimeRemaining);
    }

    [Fact]
    public void EstimatedTimeRemaining_AfterFirstCompletion_IsNotNull()
    {
        var estimator = new ProgressEstimator(5);

        // Simulate some work time
        Thread.Sleep(10);
        estimator.RecordCompletion();

        var remaining = estimator.EstimatedTimeRemaining;
        Assert.NotNull(remaining);
        Assert.True(remaining.Value > TimeSpan.Zero);
    }

    [Fact]
    public void EstimatedTimeRemaining_DecreasesAsItemsComplete()
    {
        var estimator = new ProgressEstimator(5);

        Thread.Sleep(10);
        estimator.RecordCompletion();
        var remaining1 = estimator.EstimatedTimeRemaining;

        Thread.Sleep(10);
        estimator.RecordCompletion();
        var remaining2 = estimator.EstimatedTimeRemaining;

        Assert.NotNull(remaining1);
        Assert.NotNull(remaining2);
        // With 4 remaining vs 3 remaining and similar per-item times,
        // the second estimate should be less
        Assert.True(remaining2.Value < remaining1.Value);
    }

    [Fact]
    public void SingleItem_CompletionGives100Percent()
    {
        var estimator = new ProgressEstimator(1);

        estimator.RecordCompletion();

        Assert.Equal(100.0, estimator.ProgressPercentage);
        Assert.Equal(1, estimator.CompletedCount);
    }

    [Fact]
    public void MovingAverageWindow_LimitedToTenItems()
    {
        // With 15 total items, after completing 12, the moving average
        // should only consider the last 10 durations
        var estimator = new ProgressEstimator(15);

        for (int i = 0; i < 12; i++)
        {
            Thread.Sleep(1);
            estimator.RecordCompletion();
        }

        // Should still provide an estimate for the remaining 3 items
        var remaining = estimator.EstimatedTimeRemaining;
        Assert.NotNull(remaining);
        Assert.True(remaining.Value > TimeSpan.Zero);
        Assert.Equal(12, estimator.CompletedCount);
    }

    [Fact]
    public void TotalCount_IsImmutable()
    {
        var estimator = new ProgressEstimator(10);

        estimator.RecordCompletion();
        estimator.RecordCompletion();

        Assert.Equal(10, estimator.TotalCount);
    }

    [Fact]
    public void ProgressPercentage_IsAccurate_ForVariousTotals()
    {
        var estimator = new ProgressEstimator(3);

        estimator.RecordCompletion();
        Assert.Equal(100.0 / 3.0, estimator.ProgressPercentage, precision: 10);

        estimator.RecordCompletion();
        Assert.Equal(200.0 / 3.0, estimator.ProgressPercentage, precision: 10);

        estimator.RecordCompletion();
        Assert.Equal(100.0, estimator.ProgressPercentage, precision: 10);
    }
}
