using System.Diagnostics;
using System.IO;
using System.Text.Json;
using VideoManager.Models;

namespace VideoManager.Services;

public class FFmpegService : IFFmpegService
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

    private readonly string _ffprobePath;
    private readonly string _ffmpegPath;

    public FFmpegService(string? ffprobePath = null, string? ffmpegPath = null)
    {
        _ffprobePath = ffprobePath ?? "ffprobe";
        _ffmpegPath = ffmpegPath ?? "ffmpeg";
    }

    public async Task<bool> CheckAvailabilityAsync()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cts.Token);

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<VideoMetadata> ExtractMetadataAsync(string videoPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            throw new ArgumentException("Video path cannot be null or empty.", nameof(videoPath));

        if (!File.Exists(videoPath))
            throw new FileNotFoundException("Video file not found.", videoPath);

        var arguments = $"-v quiet -print_format json -show_format -show_streams \"{videoPath}\"";

        var jsonOutput = await RunProcessAsync(_ffprobePath, arguments, ct);

        return ParseFfprobeOutput(jsonOutput);
    }

    public async Task<string> GenerateThumbnailAsync(string videoPath, string outputDir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            throw new ArgumentException("Video path cannot be null or empty.", nameof(videoPath));

        if (!File.Exists(videoPath))
            throw new FileNotFoundException("Video file not found.", videoPath);

        if (string.IsNullOrWhiteSpace(outputDir))
            throw new ArgumentException("Output directory cannot be null or empty.", nameof(outputDir));

        Directory.CreateDirectory(outputDir);

        var thumbnailFileName = $"{Path.GetFileNameWithoutExtension(videoPath)}_{Guid.NewGuid():N}.jpg";
        var thumbnailPath = Path.Combine(outputDir, thumbnailFileName);

        // Try to extract a frame at 1 second into the video.
        // If the video is shorter than 1 second, ffmpeg will still grab the first available frame.
        var arguments = $"-y -i \"{videoPath}\" -ss 00:00:01 -vframes 1 -q:v 2 \"{thumbnailPath}\"";

        await RunProcessAsync(_ffmpegPath, arguments, ct);

        if (!File.Exists(thumbnailPath))
            throw new InvalidOperationException($"Thumbnail generation failed. Output file was not created: {thumbnailPath}");

        return thumbnailPath;
    }

    internal static VideoMetadata ParseFfprobeOutput(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var duration = TimeSpan.Zero;
        int width = 0;
        int height = 0;
        string codec = string.Empty;
        long bitrate = 0;

        // Extract duration and bitrate from format section
        if (root.TryGetProperty("format", out var format))
        {
            if (format.TryGetProperty("duration", out var durationProp))
            {
                var durationStr = durationProp.GetString();
                if (double.TryParse(durationStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var durationSeconds))
                {
                    duration = TimeSpan.FromSeconds(durationSeconds);
                }
            }

            if (format.TryGetProperty("bit_rate", out var bitrateProp))
            {
                var bitrateStr = bitrateProp.GetString();
                if (long.TryParse(bitrateStr, out var parsedBitrate))
                {
                    bitrate = parsedBitrate;
                }
            }
        }

        // Extract video stream info (width, height, codec)
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                if (stream.TryGetProperty("codec_type", out var codecType) &&
                    codecType.GetString() == "video")
                {
                    if (stream.TryGetProperty("width", out var widthProp))
                        width = widthProp.GetInt32();

                    if (stream.TryGetProperty("height", out var heightProp))
                        height = heightProp.GetInt32();

                    if (stream.TryGetProperty("codec_name", out var codecProp))
                        codec = codecProp.GetString() ?? string.Empty;

                    // If duration wasn't in format, try the stream
                    if (duration == TimeSpan.Zero && stream.TryGetProperty("duration", out var streamDuration))
                    {
                        var durationStr = streamDuration.GetString();
                        if (double.TryParse(durationStr, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var durationSeconds))
                        {
                            duration = TimeSpan.FromSeconds(durationSeconds);
                        }
                    }

                    break; // Use the first video stream
                }
            }
        }

        return new VideoMetadata(duration, width, height, codec, bitrate);
    }

    private async Task<string> RunProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to start process '{fileName}'. Ensure it is installed and available in PATH.", ex);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ProcessTimeout);

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Process '{fileName}' exited with code {process.ExitCode}. Error: {error}");
            }

            return output;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Timeout occurred (not user cancellation)
            KillProcess(process);
            throw new TimeoutException(
                $"Process '{fileName}' exceeded the timeout of {ProcessTimeout.TotalSeconds} seconds and was terminated.");
        }
        catch (OperationCanceledException)
        {
            // User cancellation
            KillProcess(process);
            throw;
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort — ignore errors during kill
        }
    }
}
