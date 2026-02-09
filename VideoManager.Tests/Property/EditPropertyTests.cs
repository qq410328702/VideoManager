using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for video metadata editing persistence.
/// </summary>
public class EditPropertyTests
{
    private static VideoManagerDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var context = new VideoManagerDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Generates arbitrary non-empty, non-whitespace title strings.
    /// Titles consist of alphanumeric characters and spaces, with at least one non-space character.
    /// </summary>
    private static FsCheck.Arbitrary<string> NonEmptyTitleArb()
    {
        var charGen = FsCheck.Fluent.Gen.Elements(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 _-".ToCharArray());
        var charArrayGen = FsCheck.Fluent.Gen.ArrayOf(charGen);
        var nonEmptyArrayGen = FsCheck.Fluent.Gen.Where(charArrayGen,
            arr => arr.Length > 0 && arr.Length <= 200
                   && new string(arr).Trim().Length > 0);
        var stringGen = FsCheck.Fluent.Gen.Select(nonEmptyArrayGen,
            chars => new string(chars));
        return FsCheck.Fluent.Arb.From(stringGen);
    }

    /// <summary>
    /// Generates arbitrary optional description strings (nullable).
    /// Returns either null or a non-empty string.
    /// </summary>
    private static FsCheck.Arbitrary<string?> OptionalDescriptionArb()
    {
        var charGen = FsCheck.Fluent.Gen.Elements(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,!?_-".ToCharArray());
        var charArrayGen = FsCheck.Fluent.Gen.ArrayOf(charGen);
        var descStringGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.Where(charArrayGen, arr => arr.Length > 0 && arr.Length <= 500),
            chars => new string(chars));

        // 50% chance of null, 50% chance of a non-empty description
        var nullableGen = FsCheck.Fluent.Gen.OneOf(
            FsCheck.Fluent.Gen.Constant<string?>(null),
            FsCheck.Fluent.Gen.Select(descStringGen, s => (string?)s));

        return FsCheck.Fluent.Arb.From(nullableGen);
    }

    /// <summary>
    /// **Feature: video-manager, Property 12: 元信息编辑持久化 round-trip**
    /// **Validates: Requirements 6.2**
    ///
    /// For any valid (non-empty, non-whitespace) title and optional description,
    /// after calling UpdateVideoInfoAsync, re-reading the video from the database
    /// should return the exact same title and description values.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property EditMetadataPersistenceRoundTrip()
    {
        return FsCheck.Fluent.Prop.ForAll(NonEmptyTitleArb(), newTitle =>
            FsCheck.Fluent.Prop.ForAll(OptionalDescriptionArb(), newDescription =>
            {
                using var context = CreateInMemoryContext();
                var editService = new EditService(context);
                var ct = CancellationToken.None;

                // --- Setup: create a video entry with initial values ---
                var video = new VideoEntry
                {
                    Title = "Original Title",
                    Description = "Original Description",
                    FileName = "test_video.mp4",
                    FilePath = "/videos/test_video.mp4",
                    FileSize = 1024L,
                    Duration = TimeSpan.FromMinutes(5),
                    Width = 1920,
                    Height = 1080,
                    ImportedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                context.VideoEntries.Add(video);
                context.SaveChanges();

                int videoId = video.Id;

                // --- Act: update the video info via EditService ---
                editService.UpdateVideoInfoAsync(videoId, newTitle, newDescription, ct)
                    .GetAwaiter().GetResult();

                // --- Clear the change tracker to force a fresh read from DB ---
                context.ChangeTracker.Clear();

                // --- Assert: re-read from database and verify round-trip ---
                var reloaded = context.VideoEntries
                    .AsNoTracking()
                    .FirstOrDefault(v => v.Id == videoId);

                if (reloaded is null)
                    return false;

                bool titleMatches = reloaded.Title == newTitle;
                bool descriptionMatches = reloaded.Description == newDescription;

                return titleMatches && descriptionMatches;
            })
        );
    }

    /// <summary>
    /// Generates arbitrary invalid title strings: null, empty, or whitespace-only.
    /// </summary>
    private static FsCheck.Arbitrary<string?> InvalidTitleArb()
    {
        // Generate whitespace-only strings of varying lengths (1-50 chars)
        var whitespaceChars = new[] { ' ', '\t', '\n', '\r' };
        var wsCharGen = FsCheck.Fluent.Gen.Elements(whitespaceChars);
        var wsArrayGen = FsCheck.Fluent.Gen.Where(
            FsCheck.Fluent.Gen.ArrayOf(wsCharGen),
            arr => arr.Length > 0 && arr.Length <= 50);
        var whitespaceStringGen = FsCheck.Fluent.Gen.Select(wsArrayGen,
            chars => (string?)new string(chars));

        // Combine null, empty string, and whitespace-only strings
        var invalidGen = FsCheck.Fluent.Gen.OneOf(
            FsCheck.Fluent.Gen.Constant<string?>(null),
            FsCheck.Fluent.Gen.Constant<string?>(string.Empty),
            whitespaceStringGen);

        return FsCheck.Fluent.Arb.From(invalidGen);
    }

    /// <summary>
    /// **Feature: video-manager, Property 13: 标题非空验证**
    /// **Validates: Requirements 6.4**
    ///
    /// For any title that is null, empty, or whitespace-only, calling UpdateVideoInfoAsync
    /// should throw an ArgumentException. The original video data should remain unchanged.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property EmptyOrWhitespaceTitleShouldBeRejected()
    {
        return FsCheck.Fluent.Prop.ForAll(InvalidTitleArb(), invalidTitle =>
        {
            using var context = CreateInMemoryContext();
            var editService = new EditService(context);
            var ct = CancellationToken.None;

            // --- Setup: create a video entry with known initial values ---
            var originalTitle = "Original Title";
            var originalDescription = "Original Description";
            var video = new VideoEntry
            {
                Title = originalTitle,
                Description = originalDescription,
                FileName = "test_video.mp4",
                FilePath = "/videos/test_video.mp4",
                FileSize = 2048L,
                Duration = TimeSpan.FromMinutes(10),
                Width = 1920,
                Height = 1080,
                ImportedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            context.VideoEntries.Add(video);
            context.SaveChanges();

            int videoId = video.Id;

            // --- Act: attempt to update with invalid title ---
            bool threwArgumentException = false;
            try
            {
                editService.UpdateVideoInfoAsync(videoId, invalidTitle!, "New Description", ct)
                    .GetAwaiter().GetResult();
            }
            catch (ArgumentException)
            {
                threwArgumentException = true;
            }

            // --- Clear the change tracker to force a fresh read from DB ---
            context.ChangeTracker.Clear();

            // --- Assert: verify exception was thrown and original data is unchanged ---
            var reloaded = context.VideoEntries
                .AsNoTracking()
                .FirstOrDefault(v => v.Id == videoId);

            if (reloaded is null)
                return false;

            bool titleUnchanged = reloaded.Title == originalTitle;
            bool descriptionUnchanged = reloaded.Description == originalDescription;

            return threwArgumentException && titleUnchanged && descriptionUnchanged;
        });
    }

}
