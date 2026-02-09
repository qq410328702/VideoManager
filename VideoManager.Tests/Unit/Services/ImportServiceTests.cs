using System.IO;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

public class ImportServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _videoLibraryDir;
    private readonly string _thumbnailDir;
    private readonly Mock<IFFmpegService> _mockFfmpeg;
    private readonly Mock<IVideoRepository> _mockVideoRepo;
    private readonly ImportService _service;

    public ImportServiceTests()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "ImportServiceTests_" + Guid.NewGuid().ToString("N"));
        _testDir = Path.Combine(baseDir, "source");
        _videoLibraryDir = Path.Combine(baseDir, "library");
        _thumbnailDir = Path.Combine(baseDir, "thumbnails");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_videoLibraryDir);
        Directory.CreateDirectory(_thumbnailDir);

        _mockFfmpeg = new Mock<IFFmpegService>();
        _mockVideoRepo = new Mock<IVideoRepository>();

        // Default: metadata extraction returns valid metadata
        _mockFfmpeg.Setup(f => f.ExtractMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VideoMetadata(TimeSpan.FromSeconds(120), 1920, 1080, "h264", 5000000));

        // Default: thumbnail generation returns a path
        _mockFfmpeg.Setup(f => f.GenerateThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string videoPath, string outDir, CancellationToken _) =>
            {
                var thumbPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(videoPath) + ".jpg");
                File.WriteAllBytes(thumbPath, new byte[] { 0xFF, 0xD8 });
                return thumbPath;
            });

        // Default: AddAsync returns the entry with an Id assigned
        _mockVideoRepo.Setup(r => r.AddAsync(It.IsAny<VideoEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((VideoEntry entry, CancellationToken _) =>
            {
                entry.Id = 1;
                return entry;
            });

        _service = new ImportService(_mockFfmpeg.Object, _mockVideoRepo.Object, Options.Create(new VideoManagerOptions
        {
            VideoLibraryPath = _videoLibraryDir,
            ThumbnailDirectory = _thumbnailDir
        }));
    }

    public void Dispose()
    {
        var baseDir = Path.GetDirectoryName(_testDir)!;
        if (Directory.Exists(baseDir))
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    private string CreateTestFile(string fileName, byte[]? content = null)
    {
        var filePath = Path.Combine(_testDir, fileName);
        File.WriteAllBytes(filePath, content ?? new byte[] { 0x00, 0x01 });
        return filePath;
    }

    #region ScanFolderAsync — argument validation

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ScanFolderAsync_NullOrEmptyPath_ThrowsArgumentException(string? path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ScanFolderAsync(path!, CancellationToken.None));
    }

    [Fact]
    public async Task ScanFolderAsync_NonExistentFolder_ThrowsDirectoryNotFoundException()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            _service.ScanFolderAsync(nonExistentPath, CancellationToken.None));
    }

    #endregion

    #region ScanFolderAsync — empty folder

    [Fact]
    public async Task ScanFolderAsync_EmptyFolder_ReturnsEmptyList()
    {
        var result = await _service.ScanFolderAsync(_testDir, CancellationToken.None);

        Assert.Empty(result);
    }

    #endregion

    #region ScanFolderAsync — supported formats

    [Theory]
    [InlineData(".mp4")]
    [InlineData(".avi")]
    [InlineData(".mkv")]
    [InlineData(".mov")]
    [InlineData(".wmv")]
    public async Task ScanFolderAsync_SupportedFormat_ReturnsFile(string extension)
    {
        var filePath = Path.Combine(_testDir, $"video{extension}");
        await File.WriteAllBytesAsync(filePath, new byte[] { 0x00, 0x01 });

        var result = await _service.ScanFolderAsync(_testDir, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal($"video{extension}", result[0].FileName);
    }

    [Theory]
    [InlineData(".MP4")]
    [InlineData(".Avi")]
    [InlineData(".MKV")]
    [InlineData(".Mov")]
    [InlineData(".WMV")]
    public async Task ScanFolderAsync_SupportedFormatCaseInsensitive_ReturnsFile(string extension)
    {
        var filePath = Path.Combine(_testDir, $"video{extension}");
        await File.WriteAllBytesAsync(filePath, new byte[] { 0x00, 0x01 });

        var result = await _service.ScanFolderAsync(_testDir, CancellationToken.None);

        Assert.Single(result);
    }

    #endregion

    #region ScanFolderAsync — unsupported formats filtered out

    [Theory]
    [InlineData(".txt")]
    [InlineData(".jpg")]
    [InlineData(".png")]
    [InlineData(".pdf")]
    [InlineData(".exe")]
    [InlineData(".mp3")]
    [InlineData(".doc")]
    public async Task ScanFolderAsync_UnsupportedFormat_ReturnsEmptyList(string extension)
    {
        var filePath = Path.Combine(_testDir, $"file{extension}");
        await File.WriteAllBytesAsync(filePath, new byte[] { 0x00, 0x01 });

        var result = await _service.ScanFolderAsync(_testDir, CancellationToken.None);

        Assert.Empty(result);
    }

    #endregion

    #region ScanFolderAsync — mixed files

    [Fact]
    public async Task ScanFolderAsync_MixedFiles_ReturnsOnlySupportedFormats()
    {
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "movie.mp4"), new byte[] { 0x00 });
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "clip.avi"), new byte[] { 0x00 });
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "readme.txt"), new byte[] { 0x00 });
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "photo.jpg"), new byte[] { 0x00 });
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "song.mp3"), new byte[] { 0x00 });

        var result = await _service.ScanFolderAsync(_testDir, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, f => f.FileName == "movie.mp4");
        Assert.Contains(result, f => f.FileName == "clip.avi");
    }

    #endregion

    #region ScanFolderAsync — recursive scanning

    [Fact]
    public async Task ScanFolderAsync_NestedFolders_ScansRecursively()
    {
        var subDir1 = Path.Combine(_testDir, "sub1");
        var subDir2 = Path.Combine(_testDir, "sub1", "sub2");
        Directory.CreateDirectory(subDir1);
        Directory.CreateDirectory(subDir2);

        await File.WriteAllBytesAsync(Path.Combine(_testDir, "root.mp4"), new byte[] { 0x00 });
        await File.WriteAllBytesAsync(Path.Combine(subDir1, "level1.mkv"), new byte[] { 0x00 });
        await File.WriteAllBytesAsync(Path.Combine(subDir2, "level2.mov"), new byte[] { 0x00 });

        var result = await _service.ScanFolderAsync(_testDir, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, f => f.FileName == "root.mp4");
        Assert.Contains(result, f => f.FileName == "level1.mkv");
        Assert.Contains(result, f => f.FileName == "level2.mov");
    }

    #endregion

    #region ScanFolderAsync — file info correctness

    [Fact]
    public async Task ScanFolderAsync_ReturnsCorrectFileInfo()
    {
        var content = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        var filePath = Path.Combine(_testDir, "test.mp4");
        await File.WriteAllBytesAsync(filePath, content);

        var result = await _service.ScanFolderAsync(_testDir, CancellationToken.None);

        Assert.Single(result);
        var file = result[0];
        Assert.Equal("test.mp4", file.FileName);
        Assert.Equal(5L, file.FileSize);
        Assert.Equal(Path.GetFullPath(filePath), file.FilePath);
    }

    #endregion

    #region ScanFolderAsync — cancellation

    [Fact]
    public async Task ScanFolderAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        for (int i = 0; i < 10; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(_testDir, $"video{i}.mp4"), new byte[] { 0x00 });
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.ScanFolderAsync(_testDir, cts.Token));
    }

    #endregion

    #region ScanFolderAsync — files with no extension

    [Fact]
    public async Task ScanFolderAsync_FilesWithNoExtension_AreExcluded()
    {
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "noextension"), new byte[] { 0x00 });

        var result = await _service.ScanFolderAsync(_testDir, CancellationToken.None);

        Assert.Empty(result);
    }

    #endregion

    #region ImportVideosAsync — successful copy import

    [Fact]
    public async Task ImportVideosAsync_CopyMode_CopiesFileToLibraryAndCreatesEntry()
    {
        var srcPath = CreateTestFile("movie.mp4");
        var files = new List<VideoFileInfo> { new(srcPath, "movie.mp4", 2) };
        var progress = new Progress<ImportProgress>();

        var result = await _service.ImportVideosAsync(files, ImportMode.Copy, progress, CancellationToken.None);

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailCount);
        Assert.Empty(result.Errors);

        // Source file should still exist (copy mode)
        Assert.True(File.Exists(srcPath));

        // File should exist in library
        Assert.True(File.Exists(Path.Combine(_videoLibraryDir, "movie.mp4")));

        // VideoRepository.AddAsync should have been called once
        _mockVideoRepo.Verify(r => r.AddAsync(It.Is<VideoEntry>(e =>
            e.FileName == "movie.mp4" &&
            e.Title == "movie" &&
            e.OriginalFileName == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ImportVideosAsync — successful move import

    [Fact]
    public async Task ImportVideosAsync_MoveMode_MovesFileToLibrary()
    {
        var srcPath = CreateTestFile("clip.avi");
        var files = new List<VideoFileInfo> { new(srcPath, "clip.avi", 2) };
        var progress = new Progress<ImportProgress>();

        var result = await _service.ImportVideosAsync(files, ImportMode.Move, progress, CancellationToken.None);

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailCount);

        // Source file should NOT exist (move mode)
        Assert.False(File.Exists(srcPath));

        // File should exist in library
        Assert.True(File.Exists(Path.Combine(_videoLibraryDir, "clip.avi")));
    }

    #endregion

    #region ImportVideosAsync — auto-rename on conflict

    [Fact]
    public async Task ImportVideosAsync_DuplicateFileName_AutoRenames()
    {
        // Pre-create a file in the library with the same name
        File.WriteAllBytes(Path.Combine(_videoLibraryDir, "video.mp4"), new byte[] { 0xFF });

        var srcPath = CreateTestFile("video.mp4");
        var files = new List<VideoFileInfo> { new(srcPath, "video.mp4", 2) };
        var progress = new Progress<ImportProgress>();

        var result = await _service.ImportVideosAsync(files, ImportMode.Copy, progress, CancellationToken.None);

        Assert.Equal(1, result.SuccessCount);

        // The renamed file should exist
        Assert.True(File.Exists(Path.Combine(_videoLibraryDir, "video_1.mp4")));

        // OriginalFileName should be recorded
        _mockVideoRepo.Verify(r => r.AddAsync(It.Is<VideoEntry>(e =>
            e.FileName == "video_1.mp4" &&
            e.OriginalFileName == "video.mp4"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportVideosAsync_MultipleDuplicates_IncrementsCounter()
    {
        // Pre-create files in the library
        File.WriteAllBytes(Path.Combine(_videoLibraryDir, "video.mp4"), new byte[] { 0xFF });
        File.WriteAllBytes(Path.Combine(_videoLibraryDir, "video_1.mp4"), new byte[] { 0xFF });

        var srcPath = CreateTestFile("video.mp4");
        var files = new List<VideoFileInfo> { new(srcPath, "video.mp4", 2) };
        var progress = new Progress<ImportProgress>();

        var result = await _service.ImportVideosAsync(files, ImportMode.Copy, progress, CancellationToken.None);

        Assert.Equal(1, result.SuccessCount);
        Assert.True(File.Exists(Path.Combine(_videoLibraryDir, "video_2.mp4")));
    }

    #endregion

    #region ImportVideosAsync — metadata extraction

    [Fact]
    public async Task ImportVideosAsync_ExtractsMetadataFromFFmpeg()
    {
        var expectedMetadata = new VideoMetadata(TimeSpan.FromMinutes(5), 3840, 2160, "hevc", 10000000);
        _mockFfmpeg.Setup(f => f.ExtractMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMetadata);

        var srcPath = CreateTestFile("hdr.mkv");
        var files = new List<VideoFileInfo> { new(srcPath, "hdr.mkv", 2) };

        var result = await _service.ImportVideosAsync(files, ImportMode.Copy, new Progress<ImportProgress>(), CancellationToken.None);

        Assert.Equal(1, result.SuccessCount);
        _mockVideoRepo.Verify(r => r.AddAsync(It.Is<VideoEntry>(e =>
            e.Duration == TimeSpan.FromMinutes(5) &&
            e.Width == 3840 &&
            e.Height == 2160 &&
            e.Codec == "hevc" &&
            e.Bitrate == 10000000
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportVideosAsync_MetadataExtractionFails_UsesDefaults()
    {
        _mockFfmpeg.Setup(f => f.ExtractMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ffprobe failed"));

        var srcPath = CreateTestFile("broken.mp4");
        var files = new List<VideoFileInfo> { new(srcPath, "broken.mp4", 2) };

        var result = await _service.ImportVideosAsync(files, ImportMode.Copy, new Progress<ImportProgress>(), CancellationToken.None);

        Assert.Equal(1, result.SuccessCount);
        _mockVideoRepo.Verify(r => r.AddAsync(It.Is<VideoEntry>(e =>
            e.Duration == TimeSpan.Zero &&
            e.Width == 0 &&
            e.Height == 0
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ImportVideosAsync — thumbnail generation

    [Fact]
    public async Task ImportVideosAsync_ThumbnailGenerationFails_SetsNullThumbnailPath()
    {
        _mockFfmpeg.Setup(f => f.GenerateThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("thumbnail failed"));

        var srcPath = CreateTestFile("video.mp4");
        var files = new List<VideoFileInfo> { new(srcPath, "video.mp4", 2) };

        var result = await _service.ImportVideosAsync(files, ImportMode.Copy, new Progress<ImportProgress>(), CancellationToken.None);

        Assert.Equal(1, result.SuccessCount);
        _mockVideoRepo.Verify(r => r.AddAsync(It.Is<VideoEntry>(e =>
            e.ThumbnailPath == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ImportVideosAsync — failure isolation

    [Fact]
    public async Task ImportVideosAsync_InvalidFilePath_SkipsAndRecordsError()
    {
        var validPath = CreateTestFile("good.mp4");
        var invalidPath = Path.Combine(_testDir, "nonexistent.mp4");

        var files = new List<VideoFileInfo>
        {
            new(invalidPath, "nonexistent.mp4", 100),
            new(validPath, "good.mp4", 2)
        };

        var result = await _service.ImportVideosAsync(files, ImportMode.Copy, new Progress<ImportProgress>(), CancellationToken.None);

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.FailCount);
        Assert.Single(result.Errors);
        Assert.Equal(invalidPath, result.Errors[0].FilePath);

        // The valid file should still be imported
        Assert.True(File.Exists(Path.Combine(_videoLibraryDir, "good.mp4")));
    }

    [Fact]
    public async Task ImportVideosAsync_AllFilesFail_ReturnsAllErrors()
    {
        var files = new List<VideoFileInfo>
        {
            new(Path.Combine(_testDir, "missing1.mp4"), "missing1.mp4", 100),
            new(Path.Combine(_testDir, "missing2.avi"), "missing2.avi", 200)
        };

        var result = await _service.ImportVideosAsync(files, ImportMode.Copy, new Progress<ImportProgress>(), CancellationToken.None);

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(2, result.FailCount);
        Assert.Equal(2, result.Errors.Count);
    }

    #endregion

    #region ImportVideosAsync — progress reporting

    [Fact]
    public async Task ImportVideosAsync_ReportsProgress()
    {
        var srcPath1 = CreateTestFile("a.mp4");
        var srcPath2 = CreateTestFile("b.mkv");
        var files = new List<VideoFileInfo>
        {
            new(srcPath1, "a.mp4", 2),
            new(srcPath2, "b.mkv", 2)
        };

        var progressReports = new List<ImportProgress>();
        var progress = new Progress<ImportProgress>(p => progressReports.Add(p));

        var result = await _service.ImportVideosAsync(files, ImportMode.Copy, progress, CancellationToken.None);

        // Allow time for Progress<T> to invoke callbacks (it posts to SynchronizationContext)
        await Task.Delay(100);

        Assert.Equal(2, result.SuccessCount);
        // Should have at least the per-file progress reports and the final report
        Assert.True(progressReports.Count >= 1);
    }

    #endregion

    #region ImportVideosAsync — cancellation

    [Fact]
    public async Task ImportVideosAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var srcPath = CreateTestFile("video.mp4");
        var files = new List<VideoFileInfo> { new(srcPath, "video.mp4", 2) };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.ImportVideosAsync(files, ImportMode.Copy, new Progress<ImportProgress>(), cts.Token));
    }

    #endregion

    #region ImportVideosAsync — empty list

    [Fact]
    public async Task ImportVideosAsync_EmptyList_ReturnsZeroCounts()
    {
        var result = await _service.ImportVideosAsync(
            new List<VideoFileInfo>(), ImportMode.Copy, new Progress<ImportProgress>(), CancellationToken.None);

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(0, result.FailCount);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region ImportVideosAsync — null files argument

    [Fact]
    public async Task ImportVideosAsync_NullFiles_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.ImportVideosAsync(null!, ImportMode.Copy, new Progress<ImportProgress>(), CancellationToken.None));
    }

    #endregion

    #region ImportVideosAsync — VideoEntry fields correctness

    [Fact]
    public async Task ImportVideosAsync_CreatesVideoEntryWithCorrectFields()
    {
        var srcPath = CreateTestFile("my_video.mov");
        var files = new List<VideoFileInfo> { new(srcPath, "my_video.mov", 2) };

        var result = await _service.ImportVideosAsync(files, ImportMode.Copy, new Progress<ImportProgress>(), CancellationToken.None);

        Assert.Equal(1, result.SuccessCount);
        _mockVideoRepo.Verify(r => r.AddAsync(It.Is<VideoEntry>(e =>
            e.Title == "my_video" &&
            e.FileName == "my_video.mov" &&
            e.FilePath == Path.Combine(_videoLibraryDir, "my_video.mov") &&
            e.FileSize == 2 &&
            e.ImportedAt != default &&
            e.CreatedAt != default
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetUniqueDestinationPath

    [Fact]
    public void GetUniqueDestinationPath_NoConflict_ReturnsOriginalPath()
    {
        var result = _service.GetUniqueDestinationPath("newfile.mp4");

        Assert.Equal(Path.Combine(_videoLibraryDir, "newfile.mp4"), result);
    }

    [Fact]
    public void GetUniqueDestinationPath_OneConflict_AppendsSuffix1()
    {
        File.WriteAllBytes(Path.Combine(_videoLibraryDir, "existing.mp4"), new byte[] { 0x00 });

        var result = _service.GetUniqueDestinationPath("existing.mp4");

        Assert.Equal(Path.Combine(_videoLibraryDir, "existing_1.mp4"), result);
    }

    [Fact]
    public void GetUniqueDestinationPath_MultipleConflicts_IncrementsCounter()
    {
        File.WriteAllBytes(Path.Combine(_videoLibraryDir, "dup.mp4"), new byte[] { 0x00 });
        File.WriteAllBytes(Path.Combine(_videoLibraryDir, "dup_1.mp4"), new byte[] { 0x00 });
        File.WriteAllBytes(Path.Combine(_videoLibraryDir, "dup_2.mp4"), new byte[] { 0x00 });

        var result = _service.GetUniqueDestinationPath("dup.mp4");

        Assert.Equal(Path.Combine(_videoLibraryDir, "dup_3.mp4"), result);
    }

    #endregion
}
