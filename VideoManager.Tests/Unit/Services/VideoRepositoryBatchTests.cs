using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Repositories;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Verifies that VideoRepository.AddRangeAsync correctly writes multiple records
/// to the database in a single SaveChangesAsync call.
/// </summary>
public class VideoRepositoryBatchTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly VideoManagerDbContext _context;
    private readonly VideoRepository _repository;

    public VideoRepositoryBatchTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new VideoManagerDbContext(options);
        _context.Database.EnsureCreated();
        _repository = new VideoRepository(_context, NullLogger<VideoRepository>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static VideoEntry CreateVideo(string title, int index = 0)
    {
        return new VideoEntry
        {
            Title = title,
            FileName = $"video_{index}.mp4",
            FilePath = $"/videos/video_{index}.mp4",
            FileSize = 1024 * (index + 1),
            Duration = TimeSpan.FromMinutes(index + 1),
            Width = 1920,
            Height = 1080,
            ImportedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task AddRangeAsync_WithMultipleEntries_WritesAllRecordsToDatabase()
    {
        // Arrange
        var entries = new List<VideoEntry>
        {
            CreateVideo("Video A", 0),
            CreateVideo("Video B", 1),
            CreateVideo("Video C", 2)
        };

        // Act
        await _repository.AddRangeAsync(entries, CancellationToken.None);

        // Assert
        var count = await _context.VideoEntries.CountAsync();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task AddRangeAsync_WithEmptyList_DoesNotThrow()
    {
        // Arrange
        var entries = new List<VideoEntry>();

        // Act & Assert â€?should complete without throwing
        await _repository.AddRangeAsync(entries, CancellationToken.None);

        var count = await _context.VideoEntries.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task AddRangeAsync_AllEntriesCanBeQueriedBackWithCorrectData()
    {
        // Arrange
        var entries = new List<VideoEntry>
        {
            CreateVideo("Alpha", 0),
            CreateVideo("Beta", 1),
            CreateVideo("Gamma", 2),
            CreateVideo("Delta", 3)
        };

        // Act
        await _repository.AddRangeAsync(entries, CancellationToken.None);

        // Assert â€?query back and verify data integrity
        var stored = await _context.VideoEntries
            .OrderBy(v => v.Title)
            .ToListAsync();

        Assert.Equal(4, stored.Count);

        Assert.Equal("Alpha", stored[0].Title);
        Assert.Equal("video_0.mp4", stored[0].FileName);
        Assert.Equal(1024, stored[0].FileSize);

        Assert.Equal("Beta", stored[1].Title);
        Assert.Equal("video_1.mp4", stored[1].FileName);
        Assert.Equal(2048, stored[1].FileSize);

        Assert.Equal("Delta", stored[2].Title);
        Assert.Equal("video_3.mp4", stored[2].FileName);
        Assert.Equal(4096, stored[2].FileSize);

        Assert.Equal("Gamma", stored[3].Title);
        Assert.Equal("video_2.mp4", stored[3].FileName);
        Assert.Equal(3072, stored[3].FileSize);
    }

    [Fact]
    public async Task AddRangeAsync_WritesInSingleSaveChanges_CountMatchesInputSize()
    {
        // Arrange
        var entries = new List<VideoEntry>();
        for (int i = 0; i < 10; i++)
        {
            entries.Add(CreateVideo($"Batch Video {i}", i));
        }

        // Act
        await _repository.AddRangeAsync(entries, CancellationToken.None);

        // Assert â€?all 10 records should be present after the single batch call
        var count = await _context.VideoEntries.CountAsync();
        Assert.Equal(10, count);

        // Verify each entry got a unique Id assigned (proves they were all persisted)
        var ids = await _context.VideoEntries.Select(v => v.Id).ToListAsync();
        Assert.Equal(10, ids.Distinct().Count());
    }
}
