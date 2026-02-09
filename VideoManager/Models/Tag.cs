namespace VideoManager.Models;

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// 可选的十六进制颜色值，如 "#FF5722"
    /// </summary>
    public string? Color { get; set; }
    public ICollection<VideoEntry> Videos { get; set; } = new List<VideoEntry>();
}
