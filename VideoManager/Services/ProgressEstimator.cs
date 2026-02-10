using System.Diagnostics;

namespace VideoManager.Services;

/// <summary>
/// Estimates remaining time for batch operations using a moving average
/// of the most recent item completion durations.
/// 
/// Uses the last 10 completed items' durations to calculate the moving average,
/// which avoids early-data bias and provides more accurate estimates as the
/// operation progresses.
/// </summary>
public class ProgressEstimator
{
    /// <summary>
    /// The number of recent item durations to use for the moving average calculation.
    /// </summary>
    internal const int MovingAverageWindowSize = 10;

    private readonly int _totalItems;
    private readonly Stopwatch _totalStopwatch;
    private readonly Queue<TimeSpan> _recentDurations;
    private Stopwatch _itemStopwatch;
    private int _completedCount;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of <see cref="ProgressEstimator"/> for a batch operation
    /// with the specified total number of items.
    /// Starts the total elapsed timer and the first per-item timer immediately.
    /// </summary>
    /// <param name="totalItems">The total number of items to process. Must be greater than zero.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="totalItems"/> is less than or equal to zero.
    /// </exception>
    public ProgressEstimator(int totalItems)
    {
        if (totalItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalItems), totalItems, "Total items must be greater than zero.");

        _totalItems = totalItems;
        _recentDurations = new Queue<TimeSpan>();
        _totalStopwatch = Stopwatch.StartNew();
        _itemStopwatch = Stopwatch.StartNew();
        _completedCount = 0;
    }

    /// <summary>
    /// Records the completion of one item. Captures the elapsed time since the last
    /// completion (or since construction for the first item), adds it to the moving
    /// average window, and starts timing the next item.
    /// 
    /// If all items have already been completed, this method does nothing.
    /// </summary>
    public void RecordCompletion()
    {
        lock (_lock)
        {
            if (_completedCount >= _totalItems)
                return;

            var elapsed = _itemStopwatch.Elapsed;

            _recentDurations.Enqueue(elapsed);
            if (_recentDurations.Count > MovingAverageWindowSize)
            {
                _recentDurations.Dequeue();
            }

            _completedCount++;

            // Restart the per-item stopwatch for the next item
            _itemStopwatch = Stopwatch.StartNew();
        }
    }

    /// <summary>
    /// Gets the estimated time remaining for the batch operation based on the
    /// moving average of the most recent item durations.
    /// 
    /// Returns <c>null</c> if no items have been completed yet (no data for estimation)
    /// or if all items are already completed.
    /// 
    /// The estimate is calculated as:
    /// <c>movingAverageDuration × remainingItemCount</c>
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            lock (_lock)
            {
                if (_completedCount == 0 || _completedCount >= _totalItems)
                    return null;

                var averageTicks = CalculateMovingAverageTicks();
                var remaining = _totalItems - _completedCount;
                return TimeSpan.FromTicks(averageTicks * remaining);
            }
        }
    }

    /// <summary>
    /// Gets the progress percentage of the batch operation.
    /// Returns a value between 0.0 and 100.0.
    /// </summary>
    public double ProgressPercentage
    {
        get
        {
            lock (_lock)
            {
                return (double)_completedCount / _totalItems * 100.0;
            }
        }
    }

    /// <summary>
    /// Gets the number of items that have been completed so far.
    /// </summary>
    public int CompletedCount
    {
        get
        {
            lock (_lock)
            {
                return _completedCount;
            }
        }
    }

    /// <summary>
    /// Gets the total number of items in the batch operation.
    /// </summary>
    public int TotalCount => _totalItems;

    /// <summary>
    /// Calculates the moving average duration in ticks from the recent durations queue.
    /// Must be called within a lock.
    /// </summary>
    private long CalculateMovingAverageTicks()
    {
        var totalTicks = 0L;
        foreach (var duration in _recentDurations)
        {
            totalTicks += duration.Ticks;
        }
        return totalTicks / _recentDurations.Count;
    }
}
