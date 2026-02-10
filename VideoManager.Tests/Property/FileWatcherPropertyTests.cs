using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;
using VideoManager.ViewModels;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for file system monitoring behavior.
/// Tests Property 13 (file deletion detection) and Property 14 (file rename detection).
///
/// **Feature: video-manager-optimization, Property 13: 文件删除检测**
/// **Feature: video-manager-optimization, Property 14: 文件重命名检测**
/// **Validates: Requirements 15.2, 15.3**
///
/// These tests verify that MainViewModel correctly responds to FileWatcher events
/// by updating VideoEntry properties (IsFileMissing, FilePath) when files are
/// deleted or renamed.
/// </summary>
public class FileWatcherPropertyTests
{
    /// <summary>
    /// Creates a MainViewModel with a mock IFileWatcherService, pre-populated with
    /// the given video entries. Returns the ViewModel and the mock so tests can raise events.
    /// </summary>
    private static (MainViewModel vm, Mock<IFileWatcherService> fileWatcherMock, VideoListViewModel videoListVm)
        CreateTestSetup(List<VideoEntry> videos)
    {
        var videoRepoMock = new Mock<IVideoRepository>();
        var searchServiceMock = new Mock<ISearchService>();
        var categoryRepoMock = new Mock<ICategoryRepository>();
        var tagRepoMock = new Mock<ITagRepository>();
        var fileWatcherMock = new Mock<IFileWatcherService>();

        videoRepoMock
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(),
                It.IsAny<SortField>(), It.IsAny<SortDirection>()))
            .ReturnsAsync(new PagedResult<VideoEntry>(new List<VideoEntry>(), 0, 1, 20));

        categoryRepoMock
            .Setup(r => r.GetTreeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FolderCategory>());
        tagRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tag>());

        var videoListVm = new VideoListViewModel(videoRepoMock.Object);
        var searchVm = new SearchViewModel(searchServiceMock.Object);
        var categoryVm = new CategoryViewModel(categoryRepoMock.Object, tagRepoMock.Object);
        var options = Options.Create(new VideoManagerOptions
        {
            VideoLibraryPath = "/test/videos",
            ThumbnailDirectory = "/test/thumbnails"
        });
        var navigationServiceMock = new Mock<INavigationService>();
        var dialogServiceMock = new Mock<IDialogService>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        var vm = new MainViewModel(videoListVm, searchVm, categoryVm, fileWatcherMock.Object, options,
            navigationServiceMock.Object, dialogServiceMock.Object, serviceProviderMock.Object);

        // Pre-populate the Videos collection
        foreach (var video in videos)
        {
            videoListVm.Videos.Add(video);
        }

        return (vm, fileWatcherMock, videoListVm);
    }

    /// <summary>
    /// Generates a random file path string for testing.
    /// Format: /videos/{randomName}.mp4
    /// </summary>
    private static FsCheck.Arbitrary<string> FilePathArb()
    {
        var gen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.Choose(1, 99999),
            seed =>
            {
                var chars = "abcdefghijklmnopqrstuvwxyz0123456789";
                var rng = new Random(seed);
                var nameLen = rng.Next(3, 15);
                var name = new string(Enumerable.Range(0, nameLen)
                    .Select(_ => chars[rng.Next(chars.Length)])
                    .ToArray());
                return $"/videos/{name}.mp4";
            });

        return FsCheck.Fluent.Arb.From(gen);
    }

    /// <summary>
    /// Generates a positive integer seed for deterministic test data generation.
    /// </summary>
    private static FsCheck.Arbitrary<int> SeedArb()
    {
        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Choose(1, 99999));
    }

    /// <summary>
    /// Generates a video count between 1 and 10 for the Videos collection.
    /// </summary>
    private static FsCheck.Arbitrary<int> VideoCountArb()
    {
        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Choose(1, 10));
    }

    /// <summary>
    /// Helper to create a list of VideoEntry objects with unique file paths.
    /// </summary>
    private static List<VideoEntry> CreateVideoEntries(int seed, int count)
    {
        var rng = new Random(seed);
        var chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var entries = new List<VideoEntry>();

        for (int i = 0; i < count; i++)
        {
            var nameLen = rng.Next(3, 15);
            var name = new string(Enumerable.Range(0, nameLen)
                .Select(_ => chars[rng.Next(chars.Length)])
                .ToArray());

            entries.Add(new VideoEntry
            {
                Id = i + 1,
                Title = $"Video_{name}",
                FileName = $"{name}.mp4",
                FilePath = $"/videos/{name}_{i}.mp4",
                FileSize = 1024 * (i + 1),
                Duration = TimeSpan.FromMinutes(i + 1),
                Width = 1920,
                Height = 1080,
                ImportedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                IsFileMissing = false
            });
        }

        return entries;
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 13: 文件删除检测**
    /// **Validates: Requirements 15.2**
    ///
    /// For any monitored video file in Video_Library, when that file is deleted,
    /// the corresponding VideoEntry should be marked as IsFileMissing = true.
    ///
    /// Test strategy: Generate random video entries, pick one at random, raise
    /// FileDeleted event with its path, verify IsFileMissing becomes true and
    /// other entries remain unaffected.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property FileDeleted_MarksVideoAsFileMissing()
    {
        return FsCheck.Fluent.Prop.ForAll(SeedArb(), VideoCountArb(), (seed, count) =>
        {
            var videos = CreateVideoEntries(seed, count);
            var (vm, fileWatcherMock, videoListVm) = CreateTestSetup(videos);

            // Pick a random video to "delete" based on seed
            var rng = new Random(seed);
            var targetIndex = rng.Next(count);
            var targetVideo = videoListVm.Videos[targetIndex];
            var deletedPath = targetVideo.FilePath;

            // Precondition: target video is not already marked as missing
            if (targetVideo.IsFileMissing)
                return true; // skip degenerate case

            // Raise FileDeleted event via mock
            fileWatcherMock.Raise(
                f => f.FileDeleted += null,
                fileWatcherMock.Object,
                new FileDeletedEventArgs(deletedPath));

            // Verify: target video is now marked as file missing
            bool targetMarkedMissing = targetVideo.IsFileMissing;

            // Verify: all other videos remain unaffected (IsFileMissing still false)
            bool othersUnaffected = videoListVm.Videos
                .Where((v, idx) => idx != targetIndex)
                .All(v => !v.IsFileMissing);

            return targetMarkedMissing && othersUnaffected;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 13: 文件删除检测**
    /// **Validates: Requirements 15.2**
    ///
    /// Case-insensitive matching: When a file is deleted and the event path differs
    /// in case from the stored FilePath, the VideoEntry should still be marked as
    /// IsFileMissing = true.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property FileDeleted_CaseInsensitiveMatch()
    {
        return FsCheck.Fluent.Prop.ForAll(SeedArb(), seed =>
        {
            var videos = CreateVideoEntries(seed, 1);
            var (vm, fileWatcherMock, videoListVm) = CreateTestSetup(videos);

            var targetVideo = videoListVm.Videos[0];
            // Use upper-case version of the path to test case-insensitive matching
            var deletedPath = targetVideo.FilePath.ToUpperInvariant();

            // Raise FileDeleted event with different-case path
            fileWatcherMock.Raise(
                f => f.FileDeleted += null,
                fileWatcherMock.Object,
                new FileDeletedEventArgs(deletedPath));

            return targetVideo.IsFileMissing;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 13: 文件删除检测**
    /// **Validates: Requirements 15.2**
    ///
    /// When a FileDeleted event is raised for a path that does not match any video,
    /// no VideoEntry should be affected.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property FileDeleted_UnknownPath_NoEffect()
    {
        return FsCheck.Fluent.Prop.ForAll(SeedArb(), VideoCountArb(), (seed, count) =>
        {
            var videos = CreateVideoEntries(seed, count);
            var (vm, fileWatcherMock, videoListVm) = CreateTestSetup(videos);

            // Use a path that doesn't match any video
            var unknownPath = $"/videos/nonexistent_{seed}_unknown.mp4";

            fileWatcherMock.Raise(
                f => f.FileDeleted += null,
                fileWatcherMock.Object,
                new FileDeletedEventArgs(unknownPath));

            // Verify: no video is marked as missing
            return videoListVm.Videos.All(v => !v.IsFileMissing);
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 14: 文件重命名检测**
    /// **Validates: Requirements 15.3**
    ///
    /// For any monitored video file in Video_Library, when that file is renamed
    /// to a new path, the corresponding VideoEntry's FilePath should be updated
    /// to the new path.
    ///
    /// Test strategy: Generate random video entries, pick one at random, raise
    /// FileRenamed event with its old path and a new path, verify FilePath is
    /// updated and other entries remain unaffected.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property FileRenamed_UpdatesVideoFilePath()
    {
        return FsCheck.Fluent.Prop.ForAll(SeedArb(), VideoCountArb(), (seed, count) =>
        {
            var videos = CreateVideoEntries(seed, count);
            var (vm, fileWatcherMock, videoListVm) = CreateTestSetup(videos);

            // Pick a random video to "rename" based on seed
            var rng = new Random(seed);
            var targetIndex = rng.Next(count);
            var targetVideo = videoListVm.Videos[targetIndex];
            var oldPath = targetVideo.FilePath;
            var newPath = $"/videos/renamed_{seed}_{targetIndex}.mp4";

            // Capture other videos' original paths
            var otherPaths = videoListVm.Videos
                .Where((v, idx) => idx != targetIndex)
                .Select(v => v.FilePath)
                .ToList();

            // Raise FileRenamed event via mock
            fileWatcherMock.Raise(
                f => f.FileRenamed += null,
                fileWatcherMock.Object,
                new FileRenamedEventArgs(oldPath, newPath));

            // Verify: target video's FilePath is updated to the new path
            bool pathUpdated = string.Equals(targetVideo.FilePath, newPath, StringComparison.Ordinal);

            // Verify: all other videos' paths remain unchanged
            var currentOtherPaths = videoListVm.Videos
                .Where((v, idx) => idx != targetIndex)
                .Select(v => v.FilePath)
                .ToList();
            bool othersUnchanged = otherPaths.SequenceEqual(currentOtherPaths);

            return pathUpdated && othersUnchanged;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 14: 文件重命名检测**
    /// **Validates: Requirements 15.3**
    ///
    /// Case-insensitive matching: When a file is renamed and the event's OldPath
    /// differs in case from the stored FilePath, the VideoEntry's FilePath should
    /// still be updated to the new path.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property FileRenamed_CaseInsensitiveMatch()
    {
        return FsCheck.Fluent.Prop.ForAll(SeedArb(), seed =>
        {
            var videos = CreateVideoEntries(seed, 1);
            var (vm, fileWatcherMock, videoListVm) = CreateTestSetup(videos);

            var targetVideo = videoListVm.Videos[0];
            // Use upper-case version of the old path to test case-insensitive matching
            var oldPathUpperCase = targetVideo.FilePath.ToUpperInvariant();
            var newPath = $"/videos/renamed_case_{seed}.mp4";

            fileWatcherMock.Raise(
                f => f.FileRenamed += null,
                fileWatcherMock.Object,
                new FileRenamedEventArgs(oldPathUpperCase, newPath));

            return string.Equals(targetVideo.FilePath, newPath, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 14: 文件重命名检测**
    /// **Validates: Requirements 15.3**
    ///
    /// When a FileRenamed event is raised for an OldPath that does not match any video,
    /// no VideoEntry should be affected.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property FileRenamed_UnknownPath_NoEffect()
    {
        return FsCheck.Fluent.Prop.ForAll(SeedArb(), VideoCountArb(), (seed, count) =>
        {
            var videos = CreateVideoEntries(seed, count);
            var (vm, fileWatcherMock, videoListVm) = CreateTestSetup(videos);

            // Capture original paths
            var originalPaths = videoListVm.Videos.Select(v => v.FilePath).ToList();

            // Use a path that doesn't match any video
            var unknownOldPath = $"/videos/nonexistent_{seed}_unknown.mp4";
            var newPath = $"/videos/renamed_unknown_{seed}.mp4";

            fileWatcherMock.Raise(
                f => f.FileRenamed += null,
                fileWatcherMock.Object,
                new FileRenamedEventArgs(unknownOldPath, newPath));

            // Verify: all video paths remain unchanged
            var currentPaths = videoListVm.Videos.Select(v => v.FilePath).ToList();
            return originalPaths.SequenceEqual(currentPaths);
        });
    }
}
