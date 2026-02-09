namespace VideoManager.Models;

public class FolderCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public FolderCategory? Parent { get; set; }
    public ICollection<FolderCategory> Children { get; set; } = new List<FolderCategory>();
    public ICollection<VideoEntry> Videos { get; set; } = new List<VideoEntry>();
}
