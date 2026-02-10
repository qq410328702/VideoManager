using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

public class WindowSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsFilePath;
    private readonly Rect _screenBounds = new(0, 0, 1920, 1080);

    public WindowSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _settingsFilePath = Path.Combine(_tempDir, "Data", "window-settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private WindowSettingsService CreateService(Rect? screenBounds = null)
    {
        var bounds = screenBounds ?? _screenBounds;
        return new WindowSettingsService(_settingsFilePath, () => bounds, NullLogger<WindowSettingsService>.Instance);
    }

    private void WriteSettingsFile(string json)
    {
        var dir = Path.GetDirectoryName(_settingsFilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_settingsFilePath, json);
    }

    #region Load — File not found

    [Fact]
    public void Load_FileDoesNotExist_ReturnsNull()
    {
        var service = CreateService();

        var result = service.Load();

        Assert.Null(result);
    }

    #endregion

    #region Load — Invalid JSON

    [Fact]
    public void Load_InvalidJson_ReturnsNull()
    {
        WriteSettingsFile("this is not valid json {{{");
        var service = CreateService();

        var result = service.Load();

        Assert.Null(result);
    }

    [Fact]
    public void Load_EmptyFile_ReturnsNull()
    {
        WriteSettingsFile("");
        var service = CreateService();

        var result = service.Load();

        Assert.Null(result);
    }

    #endregion

    #region Load — Valid settings within screen bounds

    [Fact]
    public void Load_ValidSettings_ReturnsSettings()
    {
        var json = JsonSerializer.Serialize(new
        {
            left = 100.0,
            top = 200.0,
            width = 1280.0,
            height = 720.0,
            isMaximized = false
        });
        WriteSettingsFile(json);
        var service = CreateService();

        var result = service.Load();

        Assert.NotNull(result);
        Assert.Equal(100.0, result.Left);
        Assert.Equal(200.0, result.Top);
        Assert.Equal(1280.0, result.Width);
        Assert.Equal(720.0, result.Height);
        Assert.False(result.IsMaximized);
    }

    [Fact]
    public void Load_MaximizedSettings_ReturnsIsMaximizedTrue()
    {
        var json = JsonSerializer.Serialize(new
        {
            left = 0.0,
            top = 0.0,
            width = 1920.0,
            height = 1080.0,
            isMaximized = true
        });
        WriteSettingsFile(json);
        var service = CreateService();

        var result = service.Load();

        Assert.NotNull(result);
        Assert.True(result.IsMaximized);
    }

    #endregion

    #region Load — Out of screen bounds resets to center

    [Fact]
    public void Load_WindowCompletelyOffScreenRight_ResetsToCenter()
    {
        var json = JsonSerializer.Serialize(new
        {
            left = 3000.0,
            top = 100.0,
            width = 1280.0,
            height = 720.0,
            isMaximized = false
        });
        WriteSettingsFile(json);
        var service = CreateService();

        var result = service.Load();

        Assert.NotNull(result);
        Assert.Equal(1280.0, result.Width);
        Assert.Equal(720.0, result.Height);
        Assert.False(result.IsMaximized);
        // Centered: (1920 - 1280) / 2 = 320
        Assert.Equal(320.0, result.Left);
        // Centered: (1080 - 720) / 2 = 180
        Assert.Equal(180.0, result.Top);
    }

    [Fact]
    public void Load_WindowCompletelyOffScreenLeft_ResetsToCenter()
    {
        var json = JsonSerializer.Serialize(new
        {
            left = -2000.0,
            top = 100.0,
            width = 1280.0,
            height = 720.0,
            isMaximized = false
        });
        WriteSettingsFile(json);
        var service = CreateService();

        var result = service.Load();

        Assert.NotNull(result);
        Assert.Equal(320.0, result.Left);
        Assert.Equal(180.0, result.Top);
        Assert.Equal(1280.0, result.Width);
        Assert.Equal(720.0, result.Height);
    }

    [Fact]
    public void Load_WindowCompletelyOffScreenTop_ResetsToCenter()
    {
        var json = JsonSerializer.Serialize(new
        {
            left = 100.0,
            top = -2000.0,
            width = 1280.0,
            height = 720.0,
            isMaximized = false
        });
        WriteSettingsFile(json);
        var service = CreateService();

        var result = service.Load();

        Assert.NotNull(result);
        Assert.Equal(320.0, result.Left);
        Assert.Equal(180.0, result.Top);
    }

    [Fact]
    public void Load_WindowCompletelyOffScreenBottom_ResetsToCenter()
    {
        var json = JsonSerializer.Serialize(new
        {
            left = 100.0,
            top = 2000.0,
            width = 1280.0,
            height = 720.0,
            isMaximized = false
        });
        WriteSettingsFile(json);
        var service = CreateService();

        var result = service.Load();

        Assert.NotNull(result);
        Assert.Equal(320.0, result.Left);
        Assert.Equal(180.0, result.Top);
    }

    [Fact]
    public void Load_WindowPartiallyOnScreen_DoesNotReset()
    {
        // Window extends off the right edge but left part is still visible
        var json = JsonSerializer.Serialize(new
        {
            left = 800.0,
            top = 100.0,
            width = 1280.0,
            height = 720.0,
            isMaximized = false
        });
        WriteSettingsFile(json);
        var service = CreateService();

        var result = service.Load();

        Assert.NotNull(result);
        // Should keep original position since part of window is on screen
        Assert.Equal(800.0, result.Left);
        Assert.Equal(100.0, result.Top);
    }

    #endregion

    #region Save — Creates file and directory

    [Fact]
    public void Save_CreatesDirectoryAndFile()
    {
        var service = CreateService();
        var settings = new WindowSettings(100, 200, 1280, 720, false);

        service.Save(settings);

        Assert.True(File.Exists(_settingsFilePath));
    }

    [Fact]
    public void Save_WritesCorrectJson()
    {
        var service = CreateService();
        var settings = new WindowSettings(150, 250, 1400, 800, true);

        service.Save(settings);

        var json = File.ReadAllText(_settingsFilePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var loaded = JsonSerializer.Deserialize<WindowSettings>(json, options);

        Assert.NotNull(loaded);
        Assert.Equal(150, loaded.Left);
        Assert.Equal(250, loaded.Top);
        Assert.Equal(1400, loaded.Width);
        Assert.Equal(800, loaded.Height);
        Assert.True(loaded.IsMaximized);
    }

    [Fact]
    public void Save_NullSettings_ThrowsArgumentNullException()
    {
        var service = CreateService();

        Assert.Throws<ArgumentNullException>(() => service.Save(null!));
    }

    #endregion

    #region Round-trip — Save then Load

    [Fact]
    public void RoundTrip_SaveThenLoad_ReturnsEquivalentSettings()
    {
        var service = CreateService();
        var original = new WindowSettings(300, 150, 1600, 900, false);

        service.Save(original);
        var loaded = service.Load();

        Assert.NotNull(loaded);
        Assert.Equal(original.Left, loaded.Left);
        Assert.Equal(original.Top, loaded.Top);
        Assert.Equal(original.Width, loaded.Width);
        Assert.Equal(original.Height, loaded.Height);
        Assert.Equal(original.IsMaximized, loaded.IsMaximized);
    }

    [Fact]
    public void RoundTrip_SaveMaximizedThenLoad_PreservesMaximizedState()
    {
        var service = CreateService();
        var original = new WindowSettings(0, 0, 1920, 1080, true);

        service.Save(original);
        var loaded = service.Load();

        Assert.NotNull(loaded);
        Assert.True(loaded.IsMaximized);
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var service = CreateService();
        var first = new WindowSettings(100, 100, 800, 600, false);
        var second = new WindowSettings(200, 200, 1024, 768, true);

        service.Save(first);
        service.Save(second);
        var loaded = service.Load();

        Assert.NotNull(loaded);
        Assert.Equal(200, loaded.Left);
        Assert.Equal(200, loaded.Top);
        Assert.Equal(1024, loaded.Width);
        Assert.Equal(768, loaded.Height);
        Assert.True(loaded.IsMaximized);
    }

    #endregion
}
