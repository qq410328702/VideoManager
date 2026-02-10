using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace VideoManager.Models;

public class VideoEntry : INotifyPropertyChanged
{
    public int Id { get; set; }
    [Required]
    [StringLength(500)]
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    [Required]
    public string FileName { get; set; } = string.Empty;
    public string? OriginalFileName { get; set; }

    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set => SetField(ref _filePath, value);
    }

    public string? ThumbnailPath { get; set; }
    public long FileSize { get; set; }

    /// <summary>
    /// Duration stored as ticks (long) for EF Core SQLite compatibility.
    /// Use the <see cref="Duration"/> property for TimeSpan access.
    /// </summary>
    public long DurationTicks { get; set; }

    /// <summary>
    /// Convenience property that wraps DurationTicks as a TimeSpan.
    /// Not mapped to the database.
    /// </summary>
    [NotMapped]
    public TimeSpan Duration
    {
        get => TimeSpan.FromTicks(DurationTicks);
        set => DurationTicks = value.Ticks;
    }

    public int Width { get; set; }
    public int Height { get; set; }
    public string? Codec { get; set; }
    public long Bitrate { get; set; }
    public DateTime ImportedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 软删除标记。true 表示已删除。
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// 软删除时间（UTC）。
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Marks whether the video file is missing from disk (detected by FileWatcher).
    /// Not persisted to the database.
    /// </summary>
    private bool _isFileMissing;

    [NotMapped]
    public bool IsFileMissing
    {
        get => _isFileMissing;
        set => SetField(ref _isFileMissing, value);
    }

    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    public ICollection<FolderCategory> Categories { get; set; } = new List<FolderCategory>();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
