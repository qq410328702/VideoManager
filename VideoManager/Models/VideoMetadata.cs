namespace VideoManager.Models;

public record VideoMetadata(
    TimeSpan Duration,
    int Width,
    int Height,
    string Codec,
    long Bitrate
);
