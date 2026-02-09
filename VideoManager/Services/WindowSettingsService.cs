using System.IO;
using System.Text.Json;
using System.Windows;

namespace VideoManager.Services;

/// <summary>
/// Implementation of <see cref="IWindowSettingsService"/> that persists window settings
/// as a JSON file at {AppBaseDirectory}/Data/window-settings.json.
/// </summary>
public class WindowSettingsService : IWindowSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsFilePath;
    private readonly Func<Rect> _screenBoundsProvider;

    /// <summary>
    /// Creates a new WindowSettingsService with the default settings file path
    /// and screen bounds from SystemParameters.
    /// </summary>
    public WindowSettingsService()
        : this(
            Path.Combine(AppContext.BaseDirectory, "Data", "window-settings.json"),
            () => new Rect(0, 0, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight))
    {
    }

    /// <summary>
    /// Creates a new WindowSettingsService with custom file path and screen bounds provider.
    /// Used for testing.
    /// </summary>
    /// <param name="settingsFilePath">The path to the JSON settings file.</param>
    /// <param name="screenBoundsProvider">A function that returns the current screen bounds.</param>
    internal WindowSettingsService(string settingsFilePath, Func<Rect> screenBoundsProvider)
    {
        _settingsFilePath = settingsFilePath ?? throw new ArgumentNullException(nameof(settingsFilePath));
        _screenBoundsProvider = screenBoundsProvider ?? throw new ArgumentNullException(nameof(screenBoundsProvider));
    }

    /// <inheritdoc />
    public WindowSettings? Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<WindowSettings>(json, ReadJsonOptions);

            if (settings is null)
            {
                return null;
            }

            return ValidateScreenBounds(settings);
        }
        catch (JsonException)
        {
            // JSON parse error → return null (use defaults)
            return null;
        }
        catch (IOException)
        {
            // File read error → return null (use defaults)
            return null;
        }
    }

    /// <inheritdoc />
    public void Save(WindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    /// <summary>
    /// Validates that the window position is within the current screen bounds.
    /// If the window is outside the screen, resets to centered with default size (1280×720).
    /// </summary>
    private WindowSettings ValidateScreenBounds(WindowSettings settings)
    {
        var screenBounds = _screenBoundsProvider();

        // Check if any part of the window is visible on screen
        var windowRight = settings.Left + settings.Width;
        var windowBottom = settings.Top + settings.Height;

        var isOutOfBounds =
            settings.Left >= screenBounds.Right ||
            settings.Top >= screenBounds.Bottom ||
            windowRight <= screenBounds.Left ||
            windowBottom <= screenBounds.Top;

        if (isOutOfBounds)
        {
            // Reset to center with default size
            const double defaultWidth = 1280;
            const double defaultHeight = 720;
            var centeredLeft = (screenBounds.Width - defaultWidth) / 2 + screenBounds.Left;
            var centeredTop = (screenBounds.Height - defaultHeight) / 2 + screenBounds.Top;

            return new WindowSettings(
                centeredLeft,
                centeredTop,
                defaultWidth,
                defaultHeight,
                false);
        }

        return settings;
    }
}
