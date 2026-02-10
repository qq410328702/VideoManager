using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Models;
using VideoManager.Services;
using VideoManager.ViewModels;

namespace VideoManager.Tests.Unit.ViewModels;

public class ImportViewModelTests
{
    private readonly Mock<IImportService> _importServiceMock;
    private readonly IOptions<VideoManagerOptions> _options;

    public ImportViewModelTests()
    {
        _importServiceMock = new Mock<IImportService>();
        _options = Options.Create(new VideoManagerOptions { VideoLibraryPath = "C:\\TestLibrary" });
    }

    private ImportViewModel CreateViewModel()
    {
        return new ImportViewModel(_importServiceMock.Object, _options);
    }

    private static List<VideoFileInfo> CreateScanResult(int count)
    {
        return Enumerable.Range(1, count).Select(i =>
            new VideoFileInfo($"/source/video{i}.mp4", $"video{i}.mp4", 1024 * i)
        ).ToList();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ImportViewModel(null!, _options));
    }

    [Fact]
    public void Constructor_InitializesDefaultValues()
    {
        var vm = CreateViewModel();

        Assert.NotNull(vm.ScannedFiles);
        Assert.Empty(vm.ScannedFiles);
        Assert.Equal(string.Empty, vm.SelectedFolderPath);
        Assert.Equal("C:\\TestLibrary", vm.LibraryPath);
        Assert.False(vm.MoveFiles);
        Assert.Equal(0, vm.Progress);
        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.False(vm.IsScanning);
        Assert.False(vm.IsImporting);
        Assert.Null(vm.ImportResult);
    }

    #endregion

    #region ScanFolder Tests

    [Fact]
    public async Task ScanFolderAsync_PopulatesScannedFiles()
    {
        var files = CreateScanResult(3);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync("/test/folder", It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";

        await vm.ScanFolderCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.ScannedFiles.Count);
        Assert.Equal("video1.mp4", vm.ScannedFiles[0].FileName);
        Assert.Equal("video2.mp4", vm.ScannedFiles[1].FileName);
        Assert.Equal("video3.mp4", vm.ScannedFiles[2].FileName);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task ScanFolderAsync_SetsIsScanningDuringExecution()
    {
        var tcs = new TaskCompletionSource<List<VideoFileInfo>>();
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";

        var scanTask = vm.ScanFolderCommand.ExecuteAsync(null);

        Assert.True(vm.IsScanning);

        tcs.SetResult(new List<VideoFileInfo>());
        await scanTask;

        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task ScanFolderAsync_ClearsExistingFilesBeforeScanning()
    {
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateScanResult(3));

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";

        // First scan
        await vm.ScanFolderCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.ScannedFiles.Count);

        // Second scan with different results
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateScanResult(2));

        await vm.ScanFolderCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.ScannedFiles.Count);
    }

    [Fact]
    public async Task ScanFolderAsync_OnException_SetsErrorStatusMessage()
    {
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DirectoryNotFoundException("Folder not found"));

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/nonexistent";

        await vm.ScanFolderCommand.ExecuteAsync(null);

        Assert.Contains("扫描失败", vm.StatusMessage);
        Assert.False(vm.IsScanning);
        Assert.Empty(vm.ScannedFiles);
    }

    [Fact]
    public async Task ScanFolderAsync_UpdatesStatusMessage()
    {
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateScanResult(5));

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";

        await vm.ScanFolderCommand.ExecuteAsync(null);

        Assert.Contains("5", vm.StatusMessage);
    }

    [Fact]
    public async Task ScanFolderAsync_ClearsImportResult()
    {
        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";

        // Simulate a previous import result being set
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VideoFileInfo>());

        await vm.ScanFolderCommand.ExecuteAsync(null);

        Assert.Null(vm.ImportResult);
    }

    #endregion

    #region ScanFolderCommand CanExecute Tests

    [Fact]
    public void ScanFolderCommand_CannotExecute_WhenFolderPathEmpty()
    {
        var vm = CreateViewModel();
        vm.SelectedFolderPath = "";

        Assert.False(vm.ScanFolderCommand.CanExecute(null));
    }

    [Fact]
    public void ScanFolderCommand_CannotExecute_WhenFolderPathWhitespace()
    {
        var vm = CreateViewModel();
        vm.SelectedFolderPath = "   ";

        Assert.False(vm.ScanFolderCommand.CanExecute(null));
    }

    [Fact]
    public void ScanFolderCommand_CanExecute_WhenFolderPathSet()
    {
        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";

        Assert.True(vm.ScanFolderCommand.CanExecute(null));
    }

    #endregion

    #region Import Tests

    [Fact]
    public async Task ImportAsync_CallsImportServiceWithCorrectParameters()
    {
        // Setup scan
        var files = CreateScanResult(2);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                ImportMode.Copy,
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportResult(2, 0, new List<ImportError>()));

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        _importServiceMock.Verify(s => s.ImportVideosAsync(
            It.Is<List<VideoFileInfo>>(f => f.Count == 2),
            ImportMode.Copy,
            It.IsAny<IProgress<ImportProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_UsesMoveMode_WhenMoveFilesIsTrue()
    {
        var files = CreateScanResult(1);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportResult(1, 0, new List<ImportError>()));

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        vm.MoveFiles = true;
        await vm.ScanFolderCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        _importServiceMock.Verify(s => s.ImportVideosAsync(
            It.IsAny<List<VideoFileInfo>>(),
            ImportMode.Move,
            It.IsAny<IProgress<ImportProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_SetsIsImportingDuringExecution()
    {
        var files = CreateScanResult(1);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var tcs = new TaskCompletionSource<ImportResult>();
        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        var importTask = vm.ImportCommand.ExecuteAsync(null);

        Assert.True(vm.IsImporting);

        tcs.SetResult(new ImportResult(1, 0, new List<ImportError>()));
        await importTask;

        Assert.False(vm.IsImporting);
    }

    [Fact]
    public async Task ImportAsync_SetsProgressTo100_OnCompletion()
    {
        var files = CreateScanResult(2);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportResult(2, 0, new List<ImportError>()));

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(100, vm.Progress);
    }

    [Fact]
    public async Task ImportAsync_SetsImportResult_OnCompletion()
    {
        var files = CreateScanResult(3);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var expectedResult = new ImportResult(2, 1, new List<ImportError>
        {
            new("/source/video3.mp4", "File corrupted")
        });

        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ImportResult);
        Assert.Equal(2, vm.ImportResult.SuccessCount);
        Assert.Equal(1, vm.ImportResult.FailCount);
    }

    [Fact]
    public async Task ImportAsync_WithFailures_ShowsFailureCountInStatus()
    {
        var files = CreateScanResult(3);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportResult(2, 1, new List<ImportError>
            {
                new("/source/video3.mp4", "Error")
            }));

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Contains("失败", vm.StatusMessage);
    }

    [Fact]
    public async Task ImportAsync_AllSuccess_ShowsSuccessStatus()
    {
        var files = CreateScanResult(2);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportResult(2, 0, new List<ImportError>()));

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Contains("成功", vm.StatusMessage);
        Assert.DoesNotContain("失败", vm.StatusMessage);
    }

    [Fact]
    public async Task ImportAsync_OnException_SetsErrorStatusMessage()
    {
        var files = CreateScanResult(1);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk full"));

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Contains("导入失败", vm.StatusMessage);
        Assert.False(vm.IsImporting);
    }

    #endregion

    #region ImportCommand CanExecute Tests

    [Fact]
    public void ImportCommand_CannotExecute_WhenNoScannedFiles()
    {
        var vm = CreateViewModel();
        vm.LibraryPath = "/library";

        Assert.False(vm.ImportCommand.CanExecute(null));
    }

    [Fact]
    public void ImportCommand_CannotExecute_WhenLibraryPathEmpty()
    {
        var vm = CreateViewModel();
        vm.ScannedFiles.Add(new VideoFileInfo("/test.mp4", "test.mp4", 1024));
        vm.LibraryPath = "";

        Assert.False(vm.ImportCommand.CanExecute(null));
    }

    [Fact]
    public void ImportCommand_CanExecute_WhenFilesAndLibraryPathSet()
    {
        var vm = CreateViewModel();
        vm.ScannedFiles.Add(new VideoFileInfo("/test.mp4", "test.mp4", 1024));
        vm.LibraryPath = "/library";

        Assert.True(vm.ImportCommand.CanExecute(null));
    }

    #endregion

    #region Cancel Tests

    [Fact]
    public async Task CancelCommand_CancelsScanOperation()
    {
        var tcs = new TaskCompletionSource<List<VideoFileInfo>>();
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) =>
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";

        var scanTask = vm.ScanFolderCommand.ExecuteAsync(null);

        Assert.True(vm.IsScanning);
        Assert.True(vm.CancelCommand.CanExecute(null));

        vm.CancelCommand.Execute(null);

        await scanTask;

        Assert.False(vm.IsScanning);
        Assert.Contains("取消", vm.StatusMessage);
    }

    [Fact]
    public async Task CancelCommand_CancelsImportOperation()
    {
        var files = CreateScanResult(1);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var tcs = new TaskCompletionSource<ImportResult>();
        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns((List<VideoFileInfo> _, ImportMode _, IProgress<ImportProgress> _, CancellationToken ct) =>
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        var importTask = vm.ImportCommand.ExecuteAsync(null);

        Assert.True(vm.IsImporting);
        Assert.True(vm.CancelCommand.CanExecute(null));

        vm.CancelCommand.Execute(null);

        await importTask;

        Assert.False(vm.IsImporting);
        Assert.Contains("取消", vm.StatusMessage);
    }

    [Fact]
    public void CancelCommand_CannotExecute_WhenNotScanningOrImporting()
    {
        var vm = CreateViewModel();

        Assert.False(vm.CancelCommand.CanExecute(null));
    }

    #endregion

    #region PropertyChanged Tests

    [Fact]
    public void SelectedFolderPath_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.SelectedFolderPath))
                raised = true;
        };

        vm.SelectedFolderPath = "/new/path";

        Assert.True(raised);
    }

    [Fact]
    public void Progress_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Progress))
                raised = true;
        };

        vm.Progress = 50;

        Assert.True(raised);
    }

    [Fact]
    public void StatusMessage_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.StatusMessage))
                raised = true;
        };

        vm.StatusMessage = "Testing";

        Assert.True(raised);
    }

    [Fact]
    public void MoveFiles_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.MoveFiles))
                raised = true;
        };

        vm.MoveFiles = true;

        Assert.True(raised);
    }

    #endregion

    #region Command Interaction Tests

    [Fact]
    public async Task ScanFolderCommand_CannotExecute_WhileImporting()
    {
        var files = CreateScanResult(1);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var tcs = new TaskCompletionSource<ImportResult>();
        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        var importTask = vm.ImportCommand.ExecuteAsync(null);

        Assert.False(vm.ScanFolderCommand.CanExecute(null));

        tcs.SetResult(new ImportResult(1, 0, new List<ImportError>()));
        await importTask;

        Assert.True(vm.ScanFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task ImportCommand_CannotExecute_WhileScanning()
    {
        var tcs = new TaskCompletionSource<List<VideoFileInfo>>();
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        vm.ScannedFiles.Add(new VideoFileInfo("/test.mp4", "test.mp4", 1024));

        var scanTask = vm.ScanFolderCommand.ExecuteAsync(null);

        Assert.False(vm.ImportCommand.CanExecute(null));

        tcs.SetResult(new List<VideoFileInfo>());
        await scanTask;
    }

    [Fact]
    public async Task ImportAsync_ResetsProgressToZero_BeforeStarting()
    {
        var files = CreateScanResult(1);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportResult(1, 0, new List<ImportError>()));

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        vm.Progress = 75; // Simulate leftover progress from previous import

        await vm.ScanFolderCommand.ExecuteAsync(null);

        // Capture progress at start of import
        double progressAtStart = -1;
        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => progressAtStart = vm.Progress)
            .ReturnsAsync(new ImportResult(1, 0, new List<ImportError>()));

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(0, progressAtStart);
    }

    #endregion

    #region EstimatedTimeRemaining Tests

    [Fact]
    public void EstimatedTimeRemaining_DefaultsToEmpty()
    {
        var vm = CreateViewModel();
        Assert.Equal(string.Empty, vm.EstimatedTimeRemaining);
    }

    [Fact]
    public async Task ImportAsync_ClearsEstimatedTimeRemaining_OnCompletion()
    {
        var files = CreateScanResult(2);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportResult(2, 0, new List<ImportError>()));

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.EstimatedTimeRemaining);
    }

    [Fact]
    public async Task ImportAsync_ClearsEstimatedTimeRemaining_OnCancellation()
    {
        var files = CreateScanResult(1);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var tcs = new TaskCompletionSource<ImportResult>();
        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns((List<VideoFileInfo> _, ImportMode _, IProgress<ImportProgress> _, CancellationToken ct) =>
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        var importTask = vm.ImportCommand.ExecuteAsync(null);
        vm.CancelCommand.Execute(null);
        await importTask;

        Assert.Equal(string.Empty, vm.EstimatedTimeRemaining);
    }

    [Fact]
    public async Task ImportAsync_ClearsEstimatedTimeRemaining_OnException()
    {
        var files = CreateScanResult(1);
        _importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        _importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk full"));

        var vm = CreateViewModel();
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.EstimatedTimeRemaining);
    }

    [Fact]
    public void EstimatedTimeRemaining_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.EstimatedTimeRemaining))
                raised = true;
        };

        vm.EstimatedTimeRemaining = "预计剩余 30 秒";

        Assert.True(raised);
    }

    #endregion

    #region FormatTimeRemaining Tests

    [Fact]
    public void FormatTimeRemaining_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ImportViewModel.FormatTimeRemaining(null));
    }

    [Fact]
    public void FormatTimeRemaining_Seconds_FormatsCorrectly()
    {
        var result = ImportViewModel.FormatTimeRemaining(TimeSpan.FromSeconds(45));
        Assert.Equal("预计剩余 45 秒", result);
    }

    [Fact]
    public void FormatTimeRemaining_Minutes_FormatsCorrectly()
    {
        var result = ImportViewModel.FormatTimeRemaining(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(15));
        Assert.Equal("预计剩余 3:15", result);
    }

    [Fact]
    public void FormatTimeRemaining_Hours_FormatsCorrectly()
    {
        var result = ImportViewModel.FormatTimeRemaining(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30));
        Assert.Equal("预计剩余 1:05:30", result);
    }

    [Fact]
    public void FormatTimeRemaining_ZeroSeconds_FormatsCorrectly()
    {
        var result = ImportViewModel.FormatTimeRemaining(TimeSpan.Zero);
        Assert.Equal("预计剩余 0 秒", result);
    }

    #endregion
}
