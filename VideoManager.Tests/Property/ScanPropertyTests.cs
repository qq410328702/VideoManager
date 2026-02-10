using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for file scanning format filtering.
/// </summary>
public class ScanPropertyTests
{
    /// <summary>
    /// The set of supported video extensions (lowercase) as defined in ImportService.
    /// </summary>
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mkv", ".mov", ".wmv"
    };

    /// <summary>
    /// Pool of unsupported extensions to mix with supported ones.
    /// </summary>
    private static readonly string[] UnsupportedExtensions = new[]
    {
        ".txt", ".jpg", ".png", ".pdf", ".exe", ".mp3", ".doc", ".zip", ".html", ".csv",
        ".bmp", ".gif", ".flac", ".wav", ".py", ".cs", ".xml", ".json", ".log", ".dat"
    };

    /// <summary>
    /// Pool of supported extensions (various cases to test case-insensitivity).
    /// </summary>
    private static readonly string[] SupportedExtensionVariants = new[]
    {
        ".mp4", ".MP4", ".Mp4", ".avi", ".AVI", ".Avi",
        ".mkv", ".MKV", ".Mkv", ".mov", ".MOV", ".Mov",
        ".wmv", ".WMV", ".Wmv"
    };

    /// <summary>
    /// All extensions combined for random selection.
    /// </summary>
    private static readonly string[] AllExtensions =
        SupportedExtensionVariants.Concat(UnsupportedExtensions).ToArray();

    /// <summary>
    /// Generates a random file configuration: an array of extension indices
    /// into AllExtensions, representing the files to create.
    /// Each int[] element is an index into AllExtensions.
    /// </summary>
    private static FsCheck.Arbitrary<int[]> FileExtensionIndicesArb()
    {
        // Generate 1-30 file extension indices, each in range [0, AllExtensions.Length - 1]
        var indexGen = FsCheck.Fluent.Gen.Choose(0, AllExtensions.Length - 1);
        var arrayGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.Choose(1, 30),
            count =>
            {
                var indices = new int[count];
                // We'll fill indices using a deterministic approach from the count
                return indices;
            });

        // Better approach: generate a non-empty array of indices directly
        var indicesGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(indexGen),
            arr => arr.Length == 0 ? new[] { 0 } : arr // ensure at least 1 file
        );

        return FsCheck.Fluent.Arb.From(indicesGen);
    }

    /// <summary>
    /// **Feature: video-manager, Property 1: 文件扫描格式过滤**
    /// **Validates: Requirements 1.1**
    ///
    /// For any folder structure containing files with various extensions,
    /// the scan result should only include supported video formats
    /// (MP4, AVI, MKV, MOV, WMV, case-insensitive) and no other file types.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ScanShouldOnlyReturnSupportedFormats()
    {
        var arb = FileExtensionIndicesArb();

        return FsCheck.Fluent.Prop.ForAll(arb, extensionIndices =>
        {
            // Create a unique temp directory for this test iteration
            var tempDir = Path.Combine(Path.GetTempPath(), "ScanPropTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create files with the randomly selected extensions
                int expectedSupportedCount = 0;
                var createdFiles = new List<(string FileName, string Extension, bool IsSupported)>();

                for (int i = 0; i < extensionIndices.Length; i++)
                {
                    var idx = Math.Abs(extensionIndices[i]) % AllExtensions.Length;
                    var ext = AllExtensions[idx];
                    var fileName = $"file_{i}{ext}";
                    var filePath = Path.Combine(tempDir, fileName);

                    File.WriteAllBytes(filePath, new byte[] { 0x00, 0x01 });

                    bool isSupported = SupportedExtensions.Contains(ext);
                    if (isSupported)
                        expectedSupportedCount++;

                    createdFiles.Add((fileName, ext, isSupported));
                }

                var mockMetrics = new Mock<IMetricsService>();
                mockMetrics.Setup(m => m.StartTimer(It.IsAny<string>())).Returns(new NoOpDisposable());

                // Run the scan
                var service = new ImportService(
                    new Mock<IFFmpegService>().Object,
                    new Mock<IVideoRepository>().Object,
                    Options.Create(new VideoManagerOptions
                    {
                        VideoLibraryPath = tempDir,
                        ThumbnailDirectory = tempDir
                    }),
                    mockMetrics.Object,
                    NullLogger<ImportService>.Instance);
                var result = service.ScanFolderAsync(tempDir, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Property 1: All returned files must have supported extensions
                bool allReturnedAreSupported = result.All(f =>
                {
                    var ext = Path.GetExtension(f.FileName);
                    return SupportedExtensions.Contains(ext);
                });

                // Property 2: The count of returned files must equal the count of
                // supported files we created
                bool countMatches = result.Count == expectedSupportedCount;

                // Property 3: No unsupported files should be in the result
                var unsupportedInResult = result.Where(f =>
                {
                    var ext = Path.GetExtension(f.FileName);
                    return !SupportedExtensions.Contains(ext);
                }).ToList();
                bool noUnsupportedReturned = unsupportedInResult.Count == 0;

                return allReturnedAreSupported && countMatches && noUnsupportedReturned;
            }
            finally
            {
                // Clean up the temp directory
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); }
                    catch { /* best effort cleanup */ }
                }
            }
        });
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}