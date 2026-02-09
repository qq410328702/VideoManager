using System.IO;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

public class FFmpegServiceTests
{
    #region ParseFfprobeOutput — valid JSON with all fields

    [Fact]
    public void ParseFfprobeOutput_ValidJson_ReturnsCorrectMetadata()
    {
        var json = """
        {
            "format": {
                "duration": "120.500000",
                "bit_rate": "2500000"
            },
            "streams": [
                {
                    "codec_type": "video",
                    "codec_name": "h264",
                    "width": 1920,
                    "height": 1080,
                    "duration": "120.500000"
                },
                {
                    "codec_type": "audio",
                    "codec_name": "aac"
                }
            ]
        }
        """;

        var result = FFmpegService.ParseFfprobeOutput(json);

        Assert.Equal(TimeSpan.FromSeconds(120.5), result.Duration);
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
        Assert.Equal("h264", result.Codec);
        Assert.Equal(2500000L, result.Bitrate);
    }

    #endregion

    #region ParseFfprobeOutput — missing optional fields (graceful defaults)

    [Fact]
    public void ParseFfprobeOutput_MissingOptionalFields_ReturnsDefaults()
    {
        // JSON with format but no duration/bitrate, and a video stream with no codec_name
        var json = """
        {
            "format": {},
            "streams": [
                {
                    "codec_type": "video",
                    "width": 640,
                    "height": 480
                }
            ]
        }
        """;

        var result = FFmpegService.ParseFfprobeOutput(json);

        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Equal(640, result.Width);
        Assert.Equal(480, result.Height);
        Assert.Equal(string.Empty, result.Codec);
        Assert.Equal(0L, result.Bitrate);
    }

    [Fact]
    public void ParseFfprobeOutput_EmptyFormatAndStreams_ReturnsAllDefaults()
    {
        var json = """
        {
            "format": {},
            "streams": []
        }
        """;

        var result = FFmpegService.ParseFfprobeOutput(json);

        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Equal(0, result.Width);
        Assert.Equal(0, result.Height);
        Assert.Equal(string.Empty, result.Codec);
        Assert.Equal(0L, result.Bitrate);
    }

    [Fact]
    public void ParseFfprobeOutput_NoFormatOrStreams_ReturnsAllDefaults()
    {
        var json = "{}";

        var result = FFmpegService.ParseFfprobeOutput(json);

        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Equal(0, result.Width);
        Assert.Equal(0, result.Height);
        Assert.Equal(string.Empty, result.Codec);
        Assert.Equal(0L, result.Bitrate);
    }

    #endregion

    #region ParseFfprobeOutput — duration in stream instead of format

    [Fact]
    public void ParseFfprobeOutput_DurationInStreamOnly_ParsesDurationFromStream()
    {
        var json = """
        {
            "format": {
                "bit_rate": "1000000"
            },
            "streams": [
                {
                    "codec_type": "video",
                    "codec_name": "hevc",
                    "width": 3840,
                    "height": 2160,
                    "duration": "300.750000"
                }
            ]
        }
        """;

        var result = FFmpegService.ParseFfprobeOutput(json);

        Assert.Equal(TimeSpan.FromSeconds(300.75), result.Duration);
        Assert.Equal(3840, result.Width);
        Assert.Equal(2160, result.Height);
        Assert.Equal("hevc", result.Codec);
        Assert.Equal(1000000L, result.Bitrate);
    }

    [Fact]
    public void ParseFfprobeOutput_DurationInBothFormatAndStream_PrefersFormatDuration()
    {
        var json = """
        {
            "format": {
                "duration": "60.000000"
            },
            "streams": [
                {
                    "codec_type": "video",
                    "codec_name": "h264",
                    "width": 1280,
                    "height": 720,
                    "duration": "59.950000"
                }
            ]
        }
        """;

        var result = FFmpegService.ParseFfprobeOutput(json);

        // Format duration takes precedence
        Assert.Equal(TimeSpan.FromSeconds(60.0), result.Duration);
    }

    #endregion

    #region ParseFfprobeOutput — no video stream

    [Fact]
    public void ParseFfprobeOutput_AudioOnlyNoVideoStream_ReturnsZeroDimensions()
    {
        var json = """
        {
            "format": {
                "duration": "180.000000",
                "bit_rate": "320000"
            },
            "streams": [
                {
                    "codec_type": "audio",
                    "codec_name": "mp3"
                }
            ]
        }
        """;

        var result = FFmpegService.ParseFfprobeOutput(json);

        Assert.Equal(TimeSpan.FromSeconds(180.0), result.Duration);
        Assert.Equal(0, result.Width);
        Assert.Equal(0, result.Height);
        Assert.Equal(string.Empty, result.Codec);
        Assert.Equal(320000L, result.Bitrate);
    }

    #endregion

    #region ParseFfprobeOutput — invalid/malformed data

    [Fact]
    public void ParseFfprobeOutput_InvalidJson_ThrowsJsonException()
    {
        var invalidJson = "not valid json at all";

        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            FFmpegService.ParseFfprobeOutput(invalidJson));
    }

    [Fact]
    public void ParseFfprobeOutput_NonNumericDuration_ReturnsZeroDuration()
    {
        var json = """
        {
            "format": {
                "duration": "not_a_number",
                "bit_rate": "also_not_a_number"
            },
            "streams": [
                {
                    "codec_type": "video",
                    "codec_name": "h264",
                    "width": 1920,
                    "height": 1080
                }
            ]
        }
        """;

        var result = FFmpegService.ParseFfprobeOutput(json);

        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Equal(0L, result.Bitrate);
        // Width/height/codec should still parse correctly
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
        Assert.Equal("h264", result.Codec);
    }

    #endregion

    #region ExtractMetadataAsync — argument validation

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExtractMetadataAsync_NullOrEmptyPath_ThrowsArgumentException(string? path)
    {
        var service = new FFmpegService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExtractMetadataAsync(path!, CancellationToken.None));
    }

    [Fact]
    public async Task ExtractMetadataAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        var service = new FFmpegService();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nonexistent.mp4");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.ExtractMetadataAsync(nonExistentPath, CancellationToken.None));
    }

    #endregion

    #region GenerateThumbnailAsync — argument validation

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateThumbnailAsync_NullOrEmptyPath_ThrowsArgumentException(string? path)
    {
        var service = new FFmpegService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GenerateThumbnailAsync(path!, Path.GetTempPath(), CancellationToken.None));
    }

    [Fact]
    public async Task GenerateThumbnailAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        var service = new FFmpegService();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nonexistent.mp4");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.GenerateThumbnailAsync(nonExistentPath, Path.GetTempPath(), CancellationToken.None));
    }

    #endregion
}
