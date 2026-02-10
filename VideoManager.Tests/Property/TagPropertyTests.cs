using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Repositories;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for Tag data model constraints.
/// </summary>
public class TagPropertyTests
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

    private static FsCheck.Arbitrary<string> NonEmptyTagNameArb()
    {
        var charGen = FsCheck.Fluent.Gen.Elements(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray());
        var charArrayGen = FsCheck.Fluent.Gen.ArrayOf(charGen);
        var nonEmptyArrayGen = FsCheck.Fluent.Gen.Where(charArrayGen,
            arr => arr.Length > 0 && arr.Length <= 100);
        var stringGen = FsCheck.Fluent.Gen.Select(nonEmptyArrayGen,
            chars => new string(chars));
        return FsCheck.Fluent.Arb.From(stringGen);
    }

    /// <summary>
    /// **Feature: video-manager, Property 6: Tag uniqueness constraint**
    /// **Validates: Requirements 3.1**
    ///
    /// For any Tag name, creating that Tag and then attempting to create
    /// another Tag with the same name should fail (throw exception),
    /// and only one Tag with that name should exist in the database.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property TagNameUniquenessConstraint()
    {
        return FsCheck.Fluent.Prop.ForAll(NonEmptyTagNameArb(), tagName =>
        {
            using var context = CreateInMemoryContext();

            // Create the first tag - should succeed
            var firstTag = new Tag { Name = tagName };
            context.Tags.Add(firstTag);
            context.SaveChanges();

            // Attempt to create a second tag with the same name
            var duplicateTag = new Tag { Name = tagName };
            context.Tags.Add(duplicateTag);

            bool exceptionThrown = false;
            try
            {
                context.SaveChanges();
            }
            catch (DbUpdateException)
            {
                exceptionThrown = true;
            }

            // Detach the failed entity to allow querying
            if (exceptionThrown)
            {
                context.Entry(duplicateTag).State = EntityState.Detached;
            }

            // Verify: exception was thrown AND only one tag exists
            var tagCount = context.Tags.Count(t => t.Name == tagName);
            return exceptionThrown && tagCount == 1;
        });
    }

    private static FsCheck.Arbitrary<string> NonEmptyVideoTitleArb()
    {
        var charGen = FsCheck.Fluent.Gen.Elements(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 ".ToCharArray());
        var charArrayGen = FsCheck.Fluent.Gen.ArrayOf(charGen);
        var nonEmptyArrayGen = FsCheck.Fluent.Gen.Where(charArrayGen,
            arr => arr.Length > 0 && arr.Length <= 100);
        var stringGen = FsCheck.Fluent.Gen.Select(nonEmptyArrayGen,
            chars => new string(chars));
        return FsCheck.Fluent.Arb.From(stringGen);
    }

    /// <summary>
    /// **Feature: video-manager, Property 7: Tag ¹ØÁª round-trip**
    /// **Validates: Requirements 3.2, 3.3, 6.3**
    ///
    /// For any VideoEntry and Tag, adding the Tag to the VideoEntry and querying
    /// should show the Tag in the Tags collection; removing the Tag should show
    /// it's no longer in the collection.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property TagAssociationRoundTrip()
    {
        return FsCheck.Fluent.Prop.ForAll(NonEmptyVideoTitleArb(), videoTitle =>
            FsCheck.Fluent.Prop.ForAll(NonEmptyTagNameArb(), tagName =>
            {
                using var context = CreateInMemoryContext();
                var videoRepo = new VideoRepository(context, NullLogger<VideoRepository>.Instance);

                // Create a VideoEntry
                var video = new VideoEntry
                {
                    Title = videoTitle,
                    FileName = videoTitle + ".mp4",
                    FilePath = "/videos/" + videoTitle + ".mp4",
                    ImportedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                videoRepo.AddAsync(video, CancellationToken.None).GetAwaiter().GetResult();

                // Create a Tag
                var tag = new Tag { Name = tagName };
                context.Tags.Add(tag);
                context.SaveChanges();

                // Add the Tag to the VideoEntry
                var loadedVideo = videoRepo.GetByIdAsync(video.Id, CancellationToken.None).GetAwaiter().GetResult()!;
                loadedVideo.Tags.Add(tag);
                videoRepo.UpdateAsync(loadedVideo, CancellationToken.None).GetAwaiter().GetResult();

                // Query and verify the Tag is in the Tags collection
                var queriedAfterAdd = videoRepo.GetByIdAsync(video.Id, CancellationToken.None).GetAwaiter().GetResult()!;
                bool tagFoundAfterAdd = queriedAfterAdd.Tags.Any(t => t.Id == tag.Id && t.Name == tagName);

                // Remove the Tag from the VideoEntry
                var tagToRemove = queriedAfterAdd.Tags.First(t => t.Id == tag.Id);
                queriedAfterAdd.Tags.Remove(tagToRemove);
                videoRepo.UpdateAsync(queriedAfterAdd, CancellationToken.None).GetAwaiter().GetResult();

                // Query and verify the Tag is no longer in the Tags collection
                var queriedAfterRemove = videoRepo.GetByIdAsync(video.Id, CancellationToken.None).GetAwaiter().GetResult()!;
                bool tagNotFoundAfterRemove = !queriedAfterRemove.Tags.Any(t => t.Id == tag.Id);

                // Also verify the Tag itself still exists in the database (Requirement 3.3)
                bool tagStillExists = context.Tags.Any(t => t.Id == tag.Id);

                // Also verify the VideoEntry still exists (Requirement 3.3)
                bool videoStillExists = context.VideoEntries.Any(v => v.Id == video.Id);

                return tagFoundAfterAdd && tagNotFoundAfterRemove && tagStillExists && videoStillExists;
            })
        );
    }
}