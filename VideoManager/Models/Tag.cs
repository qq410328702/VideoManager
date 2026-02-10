using System.ComponentModel.DataAnnotations;

namespace VideoManager.Models;

public class Tag
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 可选的十六进制颜色值，如 "#FF5722"
    /// </summary>
    [StringLength(9)]
    public string? Color { get; set; }

    public ICollection<VideoEntry> Videos { get; set; } = new List<VideoEntry>();
}
