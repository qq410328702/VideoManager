using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<WindowSettingsService> _logger;

    /// <summary>
    /// Creates a new WindowSettingsService with the default settings file path
    /// and screen bounds from SystemParameters.
    /// </summary>
    public WindowSettingsService(ILogger<WindowSettingsService> logger)
        : this(
            Path.Combine(AppContext.BaseDirectory, "Data", "window-settings.json"),
            () => new Rect(0, 0, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight),
            logger)
    {
    }

    /// <summary>
    /// Creates a new WindowSettingsService with custom file path and screen bounds provider.
    /// Used for testing.
    /// </summary>
    /// <param name="settingsFilePath">The path to the JSON settings file.</param>
    /// <param name="screenBoundsProvider">A function that returns the current screen bounds.</param>
    /// <param name="logger">The logger instance.</param>
    internal WindowSettingsService(string settingsFilePath, Func<Rect> screenBoundsProvider, ILogger<WindowSettingsService> logger)
    {
        _settingsFilePath = settingsFilePath ?? throw new ArgumentNullException(nameof(settingsFilePath));
        _screenBoundsProvider = screenBoundsProvider ?? throw new ArgumentNullException(nameof(screenBoundsProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public WindowSettings? Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                _logger.LogDebug("Window settings file not found: {SettingsFilePath}. Using defaults.", _settingsFilePath);
                return null;
            }

            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<WindowSettings>(json, ReadJsonOptions);

            if (settings is null)
            {
                _logger.LogDebug("Window settings file deserialized to null: {SettingsFilePath}. Using defaults.", _settingsFilePath);
                return null;
            }

            _logger.LogDebug("Window settings loaded successfully from {SettingsFilePath}.", _settingsFilePath);
            return ValidateScreenBounds(settings);
        }
        catch (JsonException ex)
        {
            // JSON parse error → return null (use defaults)
            _logger.LogError(ex, "Failed to parse window settings JSON from {SettingsFilePath}.", _settingsFilePath);
            return null;
        }
        catch (IOException ex)
        {
            // File read error → return null (use defaults)
            _logger.LogError(ex, "Failed to read window settings file from {SettingsFilePath}.", _settingsFilePath);
            return null;
        }
    }

    /// <inheritdoc />
    public void Save(WindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
            _logger.LogDebug("Window settings saved successfully to {SettingsFilePath}.", _settingsFilePath);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save window settings to {SettingsFilePath}.", _settingsFilePath);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied when saving window settings to {SettingsFilePath}.", _settingsFilePath);
            throw;
        }
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
