using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.ViewModels;

/// <summary>
/// ViewModel for the import dialog. Manages folder selection, scanning,
/// import execution with progress reporting, and cancellation support.
/// </summary>
public partial class ImportViewModel : ViewModelBase
{
    private readonly IImportService _importService;
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// The collection of scanned video files available for import.
    /// </summary>
    public ObservableCollection<VideoFileInfo> ScannedFiles { get; } = new();

    /// <summary>
    /// The path of the selected source folder to scan.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanFolderCommand))]
    private string _selectedFolderPath = string.Empty;

    /// <summary>
    /// The target library directory where videos will be imported.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _libraryPath = string.Empty;

    /// <summary>
    /// Whether to move files instead of copying them during import.
    /// </summary>
    [ObservableProperty]
    private bool _moveFiles;

    /// <summary>
    /// Import progress percentage (0-100).
    /// </summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// Status message describing the current operation.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Whether a folder scan is currently in progress.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isScanning;

    /// <summary>
    /// Whether a video import is currently in progress.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isImporting;

    /// <summary>
    /// The result of the last import operation, if any.
    /// </summary>
    [ObservableProperty]
    private ImportResult? _importResult;

    /// <summary>
    /// Creates a new ImportViewModel.
    /// </summary>
    /// <param name="importService">The import service for scanning and importing videos.</param>
    public ImportViewModel(IImportService importService, IOptions<VideoManagerOptions> options)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        LibraryPath = options.Value.VideoLibraryPath;
    }

    /// <summary>
    /// Raised when import completes successfully and the dialog should close.
    /// </summary>
    public event Action? ImportCompleted;

    /// <summary>
    /// Scans the selected folder for supported video files.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanScanFolder))]
    private async Task ScanFolderAsync()
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        var ct = _cancellationTokenSource.Token;

        IsScanning = true;
        StatusMessage = "正在扫描文件夹...";
        ScannedFiles.Clear();
        ImportResult = null;

        try
        {
            var files = await _importService.ScanFolderAsync(SelectedFolderPath, ct);

            foreach (var file in files)
            {
                ScannedFiles.Add(file);
            }

            StatusMessage = $"扫描完成，发现 {ScannedFiles.Count} 个视频文件";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "扫描已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"扫描失败: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool CanScanFolder() =>
        !string.IsNullOrWhiteSpace(SelectedFolderPath) && !IsScanning && !IsImporting;

    /// <summary>
    /// Imports the scanned video files into the library.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        var ct = _cancellationTokenSource.Token;

        IsImporting = true;
        Progress = 0;
        StatusMessage = "正在导入...";
        ImportResult = null;

        var mode = MoveFiles ? ImportMode.Move : ImportMode.Copy;
        var filesToImport = ScannedFiles.ToList();

        var progressReporter = new Progress<ImportProgress>(p =>
        {
            if (p.Total > 0)
            {
                Progress = (double)p.Completed / p.Total * 100;
            }
            StatusMessage = $"正在导入 ({p.Completed}/{p.Total}): {p.CurrentFile}";
        });

        try
        {
            var result = await _importService.ImportVideosAsync(filesToImport, mode, progressReporter, ct);

            ImportResult = result;
            Progress = 100;

            if (result.FailCount > 0)
            {
                StatusMessage = $"导入完成: {result.SuccessCount} 成功, {result.FailCount} 失败";
            }
            else
            {
                StatusMessage = $"导入完成: {result.SuccessCount} 个视频已成功导入";
            }

            if (result.SuccessCount > 0)
            {
                ImportCompleted?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "导入已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入失败: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    private bool CanImport() =>
        ScannedFiles.Count > 0 && !string.IsNullOrWhiteSpace(LibraryPath) && !IsScanning && !IsImporting;

    /// <summary>
    /// Cancels the current scan or import operation.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
    }

    private bool CanCancel() => IsScanning || IsImporting;
}
