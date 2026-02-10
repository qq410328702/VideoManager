using System.Collections.ObjectModel;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using VideoManager.Data;
using VideoManager.Services;
using VideoManager.ViewModels;

namespace VideoManager.Tests.Unit.ViewModels;

public class DiagnosticsViewModelTests : IDisposable
{
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly Mock<IBackupService> _backupServiceMock;
    private readonly Mock<IDbContextFactory<VideoManagerDbContext>> _dbContextFactoryMock;
    private readonly Mock<ILogger<DiagnosticsViewModel>> _loggerMock;
    private DiagnosticsViewModel? _viewModel;

    public DiagnosticsViewModelTests()
    {
        _metricsServiceMock = new Mock<IMetricsService>();
        _backupServiceMock = new Mock<IBackupService>();
        _dbContextFactoryMock = new Mock<IDbContextFactory<VideoManagerDbContext>>();
        _loggerMock = new Mock<ILogger<DiagnosticsViewModel>>();
    }

    private DiagnosticsViewModel CreateViewModel()
    {
        _viewModel = new DiagnosticsViewModel(
            _metricsServiceMock.Object,
            _backupServiceMock.Object,
            _dbContextFactoryMock.Object,
            _loggerMock.Object);
        return _viewModel;
    }

    public void Dispose()
    {
        _viewModel?.Dispose();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullMetricsService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DiagnosticsViewModel(
            null!,
            _backupServiceMock.Object,
            _dbContextFactoryMock.Object,
            _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullBackupService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DiagnosticsViewModel(
            _metricsServiceMock.Object,
            null!,
            _dbContextFactoryMock.Object,
            _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullDbContextFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DiagnosticsViewModel(
            _metricsServiceMock.Object,
            _backupServiceMock.Object,
            null!,
            _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DiagnosticsViewModel(
            _metricsServiceMock.Object,
            _backupServiceMock.Object,
            _dbContextFactoryMock.Object,
            null!));
    }

    [Fact]
    public void Constructor_InitializesDefaultValues()
    {
        var vm = CreateViewModel();

        Assert.Equal(0, vm.ManagedMemoryMb);
        Assert.Equal(0, vm.CacheCount);
        Assert.Equal(0.0, vm.CacheHitRate);
        Assert.Equal("N/A", vm.AvgImportTime);
        Assert.Equal("N/A", vm.AvgSearchTime);
        Assert.Equal(0, vm.DbFileSizeMb);
        Assert.Equal("N/A", vm.LastBackupTime);
        Assert.NotNull(vm.Backups);
        Assert.Empty(vm.Backups);
    }

    #endregion

    #region RefreshAsync Tests

    [Fact]
    public async Task RefreshAsync_UpdatesMemoryMetrics()
    {
        _metricsServiceMock.Setup(m => m.ManagedMemoryBytes).Returns(256L * 1024 * 1024); // 256 MB
        _metricsServiceMock.Setup(m => m.ThumbnailCacheCount).Returns(42);
        _metricsServiceMock.Setup(m => m.ThumbnailCacheHitRate).Returns(0.85);
        _metricsServiceMock.Setup(m => m.GetAverageTime("import")).Returns(TimeSpan.Zero);
        _metricsServiceMock.Setup(m => m.GetAverageTime("search")).Returns(TimeSpan.Zero);
        _backupServiceMock.Setup(b => b.ListBackups()).Returns(new List<BackupInfo>());

        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(256, vm.ManagedMemoryMb);
        Assert.Equal(42, vm.CacheCount);
        Assert.Equal(85.0, vm.CacheHitRate);
    }

    [Fact]
    public async Task RefreshAsync_UpdatesOperationTimings()
    {
        _metricsServiceMock.Setup(m => m.ManagedMemoryBytes).Returns(0L);
        _metricsServiceMock.Setup(m => m.ThumbnailCacheCount).Returns(0);
        _metricsServiceMock.Setup(m => m.ThumbnailCacheHitRate).Returns(0.0);
        _metricsServiceMock.Setup(m => m.GetAverageTime("import")).Returns(TimeSpan.FromMilliseconds(350));
        _metricsServiceMock.Setup(m => m.GetAverageTime("search")).Returns(TimeSpan.FromMilliseconds(45));
        _backupServiceMock.Setup(b => b.ListBackups()).Returns(new List<BackupInfo>());

        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("350 ms", vm.AvgImportTime);
        Assert.Equal("45 ms", vm.AvgSearchTime);
    }

    [Fact]
    public async Task RefreshAsync_WithZeroTimings_ShowsNA()
    {
        _metricsServiceMock.Setup(m => m.ManagedMemoryBytes).Returns(0L);
        _metricsServiceMock.Setup(m => m.ThumbnailCacheCount).Returns(0);
        _metricsServiceMock.Setup(m => m.ThumbnailCacheHitRate).Returns(0.0);
        _metricsServiceMock.Setup(m => m.GetAverageTime("import")).Returns(TimeSpan.Zero);
        _metricsServiceMock.Setup(m => m.GetAverageTime("search")).Returns(TimeSpan.Zero);
        _backupServiceMock.Setup(b => b.ListBackups()).Returns(new List<BackupInfo>());

        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("N/A", vm.AvgImportTime);
        Assert.Equal("N/A", vm.AvgSearchTime);
    }

    [Fact]
    public async Task RefreshAsync_UpdatesBackupInfo()
    {
        var backups = new List<BackupInfo>
        {
            new("backup1.db", new DateTime(2024, 6, 15, 10, 30, 0), 1024 * 1024),
            new("backup2.db", new DateTime(2024, 6, 14, 8, 0, 0), 512 * 1024)
        };

        _metricsServiceMock.Setup(m => m.ManagedMemoryBytes).Returns(0L);
        _metricsServiceMock.Setup(m => m.ThumbnailCacheCount).Returns(0);
        _metricsServiceMock.Setup(m => m.ThumbnailCacheHitRate).Returns(0.0);
        _metricsServiceMock.Setup(m => m.GetAverageTime("import")).Returns(TimeSpan.Zero);
        _metricsServiceMock.Setup(m => m.GetAverageTime("search")).Returns(TimeSpan.Zero);
        _backupServiceMock.Setup(b => b.ListBackups()).Returns(backups);

        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Backups.Count);
        Assert.Equal("2024-06-15 10:30:00", vm.LastBackupTime);
    }

    [Fact]
    public async Task RefreshAsync_WithNoBackups_ShowsNA()
    {
        _metricsServiceMock.Setup(m => m.ManagedMemoryBytes).Returns(0L);
        _metricsServiceMock.Setup(m => m.ThumbnailCacheCount).Returns(0);
        _metricsServiceMock.Setup(m => m.ThumbnailCacheHitRate).Returns(0.0);
        _metricsServiceMock.Setup(m => m.GetAverageTime("import")).Returns(TimeSpan.Zero);
        _metricsServiceMock.Setup(m => m.GetAverageTime("search")).Returns(TimeSpan.Zero);
        _backupServiceMock.Setup(b => b.ListBackups()).Returns(new List<BackupInfo>());

        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("N/A", vm.LastBackupTime);
        Assert.Empty(vm.Backups);
    }

    [Fact]
    public async Task RefreshAsync_WhenExceptionOccurs_DoesNotThrow()
    {
        _metricsServiceMock.Setup(m => m.ManagedMemoryBytes).Throws(new InvalidOperationException("test error"));

        var vm = CreateViewModel();

        // Should not throw - errors are caught and logged
        await vm.RefreshCommand.ExecuteAsync(null);
    }

    #endregion

    #region RestoreBackupAsync Tests

    [Fact]
    public async Task RestoreBackupAsync_WithNullBackup_DoesNotCallService()
    {
        var vm = CreateViewModel();
        await vm.RestoreBackupCommand.ExecuteAsync(null);

        _backupServiceMock.Verify(b => b.RestoreFromBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RestoreBackupAsync_CallsBackupService()
    {
        var backup = new BackupInfo("/path/to/backup.db", DateTime.Now, 1024);
        _backupServiceMock.Setup(b => b.RestoreFromBackupAsync(backup.FilePath, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _metricsServiceMock.Setup(m => m.ManagedMemoryBytes).Returns(0L);
        _metricsServiceMock.Setup(m => m.ThumbnailCacheCount).Returns(0);
        _metricsServiceMock.Setup(m => m.ThumbnailCacheHitRate).Returns(0.0);
        _metricsServiceMock.Setup(m => m.GetAverageTime(It.IsAny<string>())).Returns(TimeSpan.Zero);
        _backupServiceMock.Setup(b => b.ListBackups()).Returns(new List<BackupInfo>());

        var vm = CreateViewModel();
        await vm.RestoreBackupCommand.ExecuteAsync(backup);

        _backupServiceMock.Verify(b => b.RestoreFromBackupAsync(backup.FilePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreBackupAsync_RefreshesAfterRestore()
    {
        var backup = new BackupInfo("/path/to/backup.db", DateTime.Now, 1024);
        _backupServiceMock.Setup(b => b.RestoreFromBackupAsync(backup.FilePath, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _metricsServiceMock.Setup(m => m.ManagedMemoryBytes).Returns(128L * 1024 * 1024);
        _metricsServiceMock.Setup(m => m.ThumbnailCacheCount).Returns(10);
        _metricsServiceMock.Setup(m => m.ThumbnailCacheHitRate).Returns(0.5);
        _metricsServiceMock.Setup(m => m.GetAverageTime(It.IsAny<string>())).Returns(TimeSpan.Zero);
        _backupServiceMock.Setup(b => b.ListBackups()).Returns(new List<BackupInfo>());

        var vm = CreateViewModel();
        await vm.RestoreBackupCommand.ExecuteAsync(backup);

        // Verify that refresh was called after restore (metrics should be updated)
        Assert.Equal(128, vm.ManagedMemoryMb);
    }

    [Fact]
    public async Task RestoreBackupAsync_WhenServiceThrows_PropagatesException()
    {
        var backup = new BackupInfo("/path/to/backup.db", DateTime.Now, 1024);
        _backupServiceMock.Setup(b => b.RestoreFromBackupAsync(backup.FilePath, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Restore failed"));

        var vm = CreateViewModel();
        await Assert.ThrowsAsync<IOException>(() => vm.RestoreBackupCommand.ExecuteAsync(backup));
    }

    #endregion

    #region FormatTimeSpan Tests

    [Fact]
    public void FormatTimeSpan_MillisecondsRange_FormatsAsMs()
    {
        var result = DiagnosticsViewModel.FormatTimeSpan(TimeSpan.FromMilliseconds(150));
        Assert.Equal("150 ms", result);
    }

    [Fact]
    public void FormatTimeSpan_SecondsRange_FormatsAsSeconds()
    {
        var result = DiagnosticsViewModel.FormatTimeSpan(TimeSpan.FromSeconds(2.5));
        Assert.Equal("2.5 s", result);
    }

    [Fact]
    public void FormatTimeSpan_SubMillisecond_FormatsAsMs()
    {
        var result = DiagnosticsViewModel.FormatTimeSpan(TimeSpan.FromMilliseconds(0.5));
        Assert.Equal("0 ms", result);
    }

    [Fact]
    public void FormatTimeSpan_ExactlyOneSecond_FormatsAsSeconds()
    {
        var result = DiagnosticsViewModel.FormatTimeSpan(TimeSpan.FromMilliseconds(1000));
        Assert.Equal("1.0 s", result);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var vm = CreateViewModel();
        vm.Dispose();
        vm.Dispose(); // Should not throw
        _viewModel = null; // Prevent double dispose in test cleanup
    }

    #endregion
}
