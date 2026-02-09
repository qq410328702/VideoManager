using System.IO;
using System.Threading;
using Microsoft.Extensions.Options;
using VideoManager.Models;
using VideoManager.Repositories;

namespace VideoManager.Services;

/// <summary>
/// Implementation of <see cref="IImportService"/> that scans folders for video files
/// and imports them into the Video Library.
/// </summary>
public class ImportService : IImportService
{
    /// <summary>
    /// Supported video file extensions (case-insensitive comparison).
    /// </summary>
    internal static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".avi",
        ".mkv",
        ".mov",
        ".wmv"
    };

    private readonly IFFmpegService _ffmpegService;
    private readonly IVideoRepository _videoRepository;
    private readonly string _videoLibraryPath;
    private readonly string _thumbnailDir;

    /// <summary>
    /// Initializes a new instance of <see cref="ImportService"/>.
    /// </summary>
    /// <param name="ffmpegService">FFmpeg service for metadata extraction and thumbnail generation.</param>
    /// <param name="videoRepository">Repository for persisting video entries.</param>
    /// <param name="options">Configuration options containing video library and thumbnail directory paths.</param>
    public ImportService(IFFmpegService ffmpegService, IVideoRepository videoRepository, IOptions<VideoManagerOptions> options)
    {
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _videoRepository = videoRepository ?? throw new ArgumentNullException(nameof(videoRepository));
        ArgumentNullException.ThrowIfNull(options);
        _videoLibraryPath = options.Value.VideoLibraryPath ?? throw new ArgumentException("VideoLibraryPath must be configured.", nameof(options));
        _thumbnailDir = options.Value.ThumbnailDirectory ?? throw new ArgumentException("ThumbnailDirectory must be configured.", nameof(options));
    }

    /// <inheritdoc />
    public Task<List<VideoFileInfo>> ScanFolderAsync(string folderPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Folder path cannot be null or empty.", nameof(folderPath));

        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"The folder does not exist: {folderPath}");

        return Task.Run(() => ScanFolderInternal(folderPath, ct), ct);
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportVideosAsync(List<VideoFileInfo> files, ImportMode mode, IProgress<ImportProgress> progress, CancellationToken ct)
    {
        if (files == null) throw new ArgumentNullException(nameof(files));

        Directory.CreateDirectory(_videoLibraryPath);
        Directory.CreateDirectory(_thumbnailDir);

        // Thread-safe counters using int[] so Interlocked can be used from async methods
        var completed = new int[] { 0 };
        var successCount = new int[] { 0 };
        var errors = new List<ImportError>();
        var errorLock = new object();
        int total = files.Count;

        // Phase 1: Copy/move files serially to avoid IO contention.
        // Build a list of (file, destinationPath) for phase 2.
        var filesToProcess = new List<(VideoFileInfo File, string DestinationPath, string DestinationFileName)>();

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var file = files[i];
            progress?.Report(new ImportProgress(Volatile.Read(ref completed[0]), total, file.FileName));

            try
            {
                var destinationPath = GetUniqueDestinationPath(file.FileName);
                var destinationFileName = Path.GetFileName(destinationPath);

                if (mode == ImportMode.Copy)
                {
                    File.Copy(file.FilePath, destinationPath, overwrite: false);
                }
                else
                {
                    File.Move(file.FilePath, destinationPath);
                }

                filesToProcess.Add((file, destinationPath, destinationFileName));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lock (errorLock)
                {
                    errors.Add(new ImportError(file.FilePath, ex.Message));
                }
                Interlocked.Increment(ref completed[0]);
            }
        }

        // Phase 2: Extract metadata and generate thumbnails in parallel using SemaphoreSlim.
        var maxParallelism = Math.Max(1, Environment.ProcessorCount);
        using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);

        var tasks = filesToProcess.Select(item => ProcessFileMetadataAsync(
            item.File, item.DestinationPath, item.DestinationFileName,
            semaphore, completed, successCount, errors, errorLock,
            total, progress, ct)).ToArray();

        await Task.WhenAll(tasks);

        // Report final progress
        progress?.Report(new ImportProgress(total, total, string.Empty));

        return new ImportResult(Volatile.Read(ref successCount[0]), errors.Count, errors);
    }

    /// <summary>
    /// Processes metadata extraction and thumbnail generation for a single file,
    /// controlled by the semaphore for parallelism limiting.
    /// </summary>
    private async Task ProcessFileMetadataAsync(
        VideoFileInfo file,
        string destinationPath,
        string destinationFileName,
        SemaphoreSlim semaphore,
        int[] completed,
        int[] successCount,
        List<ImportError> errors,
        object errorLock,
        int total,
        IProgress<ImportProgress>? progress,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();

            // Extract metadata via FFmpeg
            VideoMetadata metadata;
            try
            {
                metadata = await _ffmpegService.ExtractMetadataAsync(destinationPath, ct);
            }
            catch (Exception)
            {
                // Use default metadata if extraction fails
                metadata = new VideoMetadata(TimeSpan.Zero, 0, 0, string.Empty, 0);
            }

            // Generate thumbnail via FFmpeg
            string? thumbnailPath = null;
            try
            {
                thumbnailPath = await _ffmpegService.GenerateThumbnailAsync(destinationPath, _thumbnailDir, ct);
            }
            catch (Exception)
            {
                // Thumbnail generation failed; leave as null (use default placeholder)
            }

            // Create VideoEntry database record
            var now = DateTime.UtcNow;
            var entry = new VideoEntry
            {
                Title = Path.GetFileNameWithoutExtension(file.FileName),
                FileName = destinationFileName,
                OriginalFileName = file.FileName != destinationFileName ? file.FileName : null,
                FilePath = destinationPath,
                ThumbnailPath = thumbnailPath,
                FileSize = file.FileSize,
                Duration = metadata.Duration,
                Width = metadata.Width,
                Height = metadata.Height,
                Codec = metadata.Codec,
                Bitrate = metadata.Bitrate,
                ImportedAt = now,
                CreatedAt = now
            };

            await _videoRepository.AddAsync(entry, ct);
            Interlocked.Increment(ref successCount[0]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            lock (errorLock)
            {
                errors.Add(new ImportError(file.FilePath, ex.Message));
            }
        }
        finally
        {
            semaphore.Release();
            var currentCompleted = Interlocked.Increment(ref completed[0]);
            progress?.Report(new ImportProgress(currentCompleted, total, file.FileName));
        }
    }


    /// <summary>
    /// Generates a unique file path in the Video Library directory.
    /// If a file with the same name already exists, appends _1, _2, etc. until a unique name is found.
    /// </summary>
    internal string GetUniqueDestinationPath(string fileName)
    {
        var destinationPath = Path.Combine(_videoLibraryPath, fileName);

        if (!File.Exists(destinationPath))
            return destinationPath;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        int counter = 1;

        do
        {
            destinationPath = Path.Combine(_videoLibraryPath, $"{nameWithoutExt}_{counter}{extension}");
            counter++;
        } while (File.Exists(destinationPath));

        return destinationPath;
    }

    /// <summary>
    /// Recursively scans the folder and returns all supported video files.
    /// </summary>
    private static List<VideoFileInfo> ScanFolderInternal(string folderPath, CancellationToken ct)
    {
        var results = new List<VideoFileInfo>();

        try
        {
            var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories);

            foreach (var filePath in files)
            {
                ct.ThrowIfCancellationRequested();

                var extension = Path.GetExtension(filePath);
                if (SupportedExtensions.Contains(extension))
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        results.Add(new VideoFileInfo(
                            FilePath: fileInfo.FullName,
                            FileName: fileInfo.Name,
                            FileSize: fileInfo.Length
                        ));
                    }
                    catch (IOException)
                    {
                        // Skip files that cannot be accessed (e.g., locked, permissions)
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip files without read permission
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // If the root folder itself is inaccessible, return empty list
        }

        return results;
    }
}

