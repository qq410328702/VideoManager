using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Repositories;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for FolderCategory tree structure.
/// </summary>
public class CategoryPropertyTests
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
    /// Represents a tree node blueprint used by the generator.
    /// Each node has a name and a list of child blueprints.
    /// </summary>
    public record TreeBlueprint(string Name, List<TreeBlueprint> Children);

    /// <summary>
    /// Generates a random tree configuration as an int array [rootCount, maxDepth, seed].
    /// Uses FsCheck.Fluent API compatible with FsCheck 3.x.
    /// </summary>
    private static FsCheck.Arbitrary<int[]> TreeConfigArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(1, 99999)),
            arr =>
            {
                // Ensure we have at least 3 elements for rootCount, maxDepth, seed
                var rootCount = arr.Length > 0 ? (arr[0] % 3) + 1 : 1;       // 1-3
                var maxDepth = arr.Length > 1 ? (arr[1] % 3) + 1 : 1;        // 1-3
                var seed = arr.Length > 2 ? Math.Abs(arr[2]) + 1 : 1;        // positive seed
                return new int[] { rootCount, maxDepth, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 3));
    }

    /// <summary>
    /// Builds a forest of tree blueprints deterministically from parameters.
    /// </summary>
    private static List<TreeBlueprint> BuildBlueprintForest(int rootCount, int maxDepth, ref int counter)
    {
        var roots = new List<TreeBlueprint>();
        for (int i = 0; i < rootCount; i++)
        {
            roots.Add(BuildBlueprintNode(maxDepth, ref counter));
        }
        return roots;
    }

    /// <summary>
    /// Builds a single tree node with children, using counter for unique naming
    /// and pseudo-random branching (0-3 children based on counter).
    /// </summary>
    private static TreeBlueprint BuildBlueprintNode(int remainingDepth, ref int counter)
    {
        var name = $"cat_{counter}";
        counter++;

        var children = new List<TreeBlueprint>();
        if (remainingDepth > 0)
        {
            // Determine child count: use counter modulo to get 0-3 children
            int childCount = counter % 4;
            for (int i = 0; i < childCount; i++)
            {
                children.Add(BuildBlueprintNode(remainingDepth - 1, ref counter));
            }
        }

        return new TreeBlueprint(name, children);
    }

    /// <summary>
    /// Recursively creates FolderCategory entities from a blueprint tree
    /// using the CategoryRepository.
    /// </summary>
    private static async Task CreateTreeFromBlueprint(
        List<TreeBlueprint> blueprints,
        int? parentId,
        CategoryRepository repo,
        CancellationToken ct)
    {
        foreach (var bp in blueprints)
        {
            var category = new FolderCategory
            {
                Name = bp.Name,
                ParentId = parentId
            };
            await repo.AddAsync(category, ct);

            if (bp.Children.Count > 0)
            {
                await CreateTreeFromBlueprint(bp.Children, category.Id, repo, ct);
            }
        }
    }

    /// <summary>
    /// Counts total nodes in a blueprint tree.
    /// </summary>
    private static int CountNodes(List<TreeBlueprint> blueprints)
    {
        int count = 0;
        foreach (var bp in blueprints)
        {
            count += 1 + CountNodes(bp.Children);
        }
        return count;
    }

    /// <summary>
    /// Recursively verifies that a retrieved tree matches the original blueprint:
    /// - Same number of children at each level
    /// - Names match
    /// - ParentId values are correct
    /// </summary>
    private static bool VerifyTree(
        List<FolderCategory> retrievedRoots,
        List<TreeBlueprint> blueprints,
        int? expectedParentId)
    {
        if (retrievedRoots.Count != blueprints.Count)
            return false;

        // Sort both lists by name for consistent comparison
        var sortedRetrieved = retrievedRoots.OrderBy(c => c.Name).ToList();
        var sortedBlueprints = blueprints.OrderBy(b => b.Name).ToList();

        for (int i = 0; i < sortedRetrieved.Count; i++)
        {
            var node = sortedRetrieved[i];
            var bp = sortedBlueprints[i];

            // Verify name matches
            if (node.Name != bp.Name)
                return false;

            // Verify ParentId is correct
            if (node.ParentId != expectedParentId)
                return false;

            // Recursively verify children
            if (!VerifyTree(node.Children.ToList(), bp.Children, node.Id))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Counts total nodes in a retrieved tree.
    /// </summary>
    private static int CountRetrievedNodes(List<FolderCategory> nodes)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            count += 1 + CountRetrievedNodes(node.Children.ToList());
        }
        return count;
    }

    /// <summary>
    /// **Feature: video-manager, Property 8: 分类�?round-trip**
    /// **Validates: Requirements 3.4**
    ///
    /// For any multi-level nested FolderCategory tree structure, after creating it
    /// and querying via GetTreeAsync, the full parent-child relationship structure
    /// should be correctly restored, with each node's ParentId and Children
    /// relationships correct.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property CategoryTreeRoundTrip()
    {
        var arb = TreeConfigArb();

        return FsCheck.Fluent.Prop.ForAll(arb, config =>
        {
            int rootCount = config[0];
            int maxDepth = config[1];
            int seed = config[2];

            // Build the blueprint tree from the random config
            var counter = seed;
            var blueprints = BuildBlueprintForest(rootCount, maxDepth, ref counter);

            int expectedTotalNodes = CountNodes(blueprints);

            using var context = CreateInMemoryContext();
            var repo = new CategoryRepository(context);
            var ct = CancellationToken.None;

            // Create the tree from the blueprint
            CreateTreeFromBlueprint(blueprints, null, repo, ct)
                .GetAwaiter().GetResult();

            // Clear the change tracker to avoid cached navigation properties
            context.ChangeTracker.Clear();

            // Query the tree via GetTreeAsync
            var retrievedRoots = repo.GetTreeAsync(ct).GetAwaiter().GetResult();

            // Verify: correct number of root nodes
            bool rootCountCorrect = retrievedRoots.Count == blueprints.Count;

            // Verify: total node count matches
            int retrievedTotalNodes = CountRetrievedNodes(retrievedRoots);
            bool totalCountCorrect = retrievedTotalNodes == expectedTotalNodes;

            // Verify: full tree structure matches (names, ParentId, Children)
            bool structureCorrect = VerifyTree(retrievedRoots, blueprints, null);

            return rootCountCorrect && totalCountCorrect && structureCorrect;
        });
    }

    /// <summary>
    /// **Feature: video-manager, Property 9: 多分类关�?*
    /// **Validates: Requirements 3.5**
    ///
    /// For any VideoEntry and multiple FolderCategories, after adding the VideoEntry
    /// to all these categories, querying the VideoEntry's Categories collection
    /// should contain all the added categories.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property MultipleCategoryAssociation()
    {
        // Generate a category count between 1 and 10
        var categoryCountGen = FsCheck.Fluent.Gen.Choose(1, 10);
        var arb = FsCheck.Fluent.Arb.From(categoryCountGen);

        return FsCheck.Fluent.Prop.ForAll(arb, categoryCount =>
        {
            using var context = CreateInMemoryContext();
            var videoRepo = new VideoRepository(context, NullLogger<VideoRepository>.Instance);
            var categoryRepo = new CategoryRepository(context);
            var ct = CancellationToken.None;

            // Create a VideoEntry
            var video = new VideoEntry
            {
                Title = $"TestVideo_{categoryCount}",
                FileName = $"test_{categoryCount}.mp4",
                FilePath = $"/videos/test_{categoryCount}.mp4",
                FileSize = 1024 * categoryCount,
                Duration = TimeSpan.FromMinutes(categoryCount),
                Width = 1920,
                Height = 1080,
                ImportedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            videoRepo.AddAsync(video, ct).GetAwaiter().GetResult();

            // Create multiple FolderCategories
            var categories = new List<FolderCategory>();
            for (int i = 0; i < categoryCount; i++)
            {
                var category = new FolderCategory
                {
                    Name = $"Category_{categoryCount}_{i}"
                };
                categoryRepo.AddAsync(category, ct).GetAwaiter().GetResult();
                categories.Add(category);
            }

            // Add the VideoEntry to all categories
            foreach (var category in categories)
            {
                video.Categories.Add(category);
            }
            videoRepo.UpdateAsync(video, ct).GetAwaiter().GetResult();

            // Clear the change tracker to force a fresh query from the database
            context.ChangeTracker.Clear();

            // Query the VideoEntry and verify its Categories collection
            var retrieved = videoRepo.GetByIdAsync(video.Id, ct).GetAwaiter().GetResult();

            if (retrieved is null)
                return false;

            // Verify: the retrieved video has exactly the expected number of categories
            bool countCorrect = retrieved.Categories.Count == categoryCount;

            // Verify: all added category IDs are present in the retrieved Categories
            var expectedIds = categories.Select(c => c.Id).OrderBy(id => id).ToList();
            var actualIds = retrieved.Categories.Select(c => c.Id).OrderBy(id => id).ToList();
            bool idsMatch = expectedIds.SequenceEqual(actualIds);

            return countCorrect && idsMatch;
        });
    }

    /// <summary>
    /// **Feature: video-manager, Property 10: 删除分类或标签不影响视频**
    /// **Validates: Requirements 3.6, 3.7**
    ///
    /// For any VideoEntry associated with FolderCategory or Tag, deleting that
    /// category or tag should leave the VideoEntry record and its corresponding
    /// video file path intact. The VideoEntry must still exist in the database
    /// with all its original properties unchanged.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property DeletingCategoryOrTagDoesNotAffectVideo()
    {
        // Generate: categoryCount (1-5), tagCount (1-5), seed for unique naming
        // Use ArrayOf + Select pattern (FsCheck 3.x Fluent API)
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(1, 99999)),
            arr =>
            {
                var catCount = arr.Length > 0 ? (arr[0] % 5) + 1 : 1;   // 1-5
                var tagCount = arr.Length > 1 ? (arr[1] % 5) + 1 : 1;   // 1-5
                var seed = arr.Length > 2 ? Math.Abs(arr[2]) + 1 : 1;   // positive seed
                return new int[] { catCount, tagCount, seed };
            });
        var arb = FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 3));

        return FsCheck.Fluent.Prop.ForAll(arb, config =>
        {
            int categoryCount = config[0];
            int tagCount = config[1];
            int seed = config[2];

            using var context = CreateInMemoryContext();
            var videoRepo = new VideoRepository(context, NullLogger<VideoRepository>.Instance);
            var categoryRepo = new CategoryRepository(context);
            var tagRepo = new TagRepository(context);
            var ct = CancellationToken.None;

            // Create a VideoEntry with identifiable properties
            var video = new VideoEntry
            {
                Title = $"Video_{seed}",
                Description = $"Description_{seed}",
                FileName = $"video_{seed}.mp4",
                FilePath = $"/videos/video_{seed}.mp4",
                FileSize = 1024L * seed,
                Duration = TimeSpan.FromMinutes(seed % 60 + 1),
                Width = 1920,
                Height = 1080,
                Codec = "h264",
                Bitrate = 5000000,
                ImportedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            videoRepo.AddAsync(video, ct).GetAwaiter().GetResult();

            // Create and associate FolderCategories
            var categories = new List<FolderCategory>();
            for (int i = 0; i < categoryCount; i++)
            {
                var category = new FolderCategory { Name = $"Cat_{seed}_{i}" };
                categoryRepo.AddAsync(category, ct).GetAwaiter().GetResult();
                categories.Add(category);
                video.Categories.Add(category);
            }

            // Create and associate Tags
            var tags = new List<Tag>();
            for (int i = 0; i < tagCount; i++)
            {
                var tag = new Tag { Name = $"Tag_{seed}_{i}" };
                tagRepo.AddAsync(tag, ct).GetAwaiter().GetResult();
                tags.Add(tag);
                video.Tags.Add(tag);
            }

            // Save associations
            videoRepo.UpdateAsync(video, ct).GetAwaiter().GetResult();

            // Record original video properties before deletion
            int originalVideoId = video.Id;
            string originalTitle = video.Title;
            string? originalDescription = video.Description;
            string originalFileName = video.FileName;
            string originalFilePath = video.FilePath;
            long originalFileSize = video.FileSize;
            TimeSpan originalDuration = video.Duration;
            int originalWidth = video.Width;
            int originalHeight = video.Height;

            // Delete all associated categories
            foreach (var category in categories)
            {
                context.ChangeTracker.Clear();
                categoryRepo.DeleteAsync(category.Id, ct).GetAwaiter().GetResult();
            }

            // Delete all associated tags
            foreach (var tag in tags)
            {
                context.ChangeTracker.Clear();
                tagRepo.DeleteAsync(tag.Id, ct).GetAwaiter().GetResult();
            }

            // Clear tracker and re-query the video
            context.ChangeTracker.Clear();
            var retrieved = videoRepo.GetByIdAsync(originalVideoId, ct).GetAwaiter().GetResult();

            // The VideoEntry must still exist
            if (retrieved is null)
                return false;

            // All original properties must be intact
            bool titleIntact = retrieved.Title == originalTitle;
            bool descriptionIntact = retrieved.Description == originalDescription;
            bool fileNameIntact = retrieved.FileName == originalFileName;
            bool filePathIntact = retrieved.FilePath == originalFilePath;
            bool fileSizeIntact = retrieved.FileSize == originalFileSize;
            bool durationIntact = retrieved.Duration == originalDuration;
            bool dimensionsIntact = retrieved.Width == originalWidth && retrieved.Height == originalHeight;

            // The associations should be gone (categories and tags deleted)
            bool categoriesCleared = retrieved.Categories.Count == 0;
            bool tagsCleared = retrieved.Tags.Count == 0;

            return titleIntact && descriptionIntact && fileNameIntact &&
                   filePathIntact && fileSizeIntact && durationIntact &&
                   dimensionsIntact && categoriesCleared && tagsCleared;
        });
    }
}
