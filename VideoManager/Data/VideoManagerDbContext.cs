using System.IO;
using Microsoft.EntityFrameworkCore;
using VideoManager.Models;

namespace VideoManager.Data;

public class VideoManagerDbContext : DbContext
{
    public DbSet<VideoEntry> VideoEntries => Set<VideoEntry>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<FolderCategory> FolderCategories => Set<FolderCategory>();

    public VideoManagerDbContext()
    {
    }

    public VideoManagerDbContext(DbContextOptions<VideoManagerDbContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VideoManager", "videomanager.db");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            options.UseSqlite($"Data Source={dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tag>()
            .HasIndex(t => t.Name)
            .IsUnique();

        modelBuilder.Entity<VideoEntry>()
            .HasMany(v => v.Tags)
            .WithMany(t => t.Videos)
            .UsingEntity("VideoTag");

        modelBuilder.Entity<VideoEntry>()
            .HasMany(v => v.Categories)
            .WithMany(c => c.Videos)
            .UsingEntity("VideoCategory");

        modelBuilder.Entity<FolderCategory>()
            .HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Tag.Color 列配置
        modelBuilder.Entity<Tag>()
            .Property(t => t.Color)
            .HasMaxLength(9); // "#RRGGBBAA"

        // 搜索性能索引
        modelBuilder.Entity<VideoEntry>()
            .HasIndex(v => v.Title);
        modelBuilder.Entity<VideoEntry>()
            .HasIndex(v => v.ImportedAt);
        modelBuilder.Entity<VideoEntry>()
            .HasIndex(v => v.DurationTicks);
        modelBuilder.Entity<VideoEntry>()
            .HasIndex(v => v.FileSize);

        // 软删除全局过滤器
        modelBuilder.Entity<VideoEntry>()
            .HasQueryFilter(v => !v.IsDeleted);
    }
}
