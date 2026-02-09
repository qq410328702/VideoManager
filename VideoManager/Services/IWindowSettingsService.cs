namespace VideoManager.Services;

/// <summary>
/// Service for persisting and loading window size/position settings.
/// Settings are stored as a JSON file on disk.
/// </summary>
public interface IWindowSettingsService
{
    /// <summary>
    /// Loads window settings from the configuration file.
    /// Returns null if the file does not exist or cannot be parsed.
    /// If the saved position is outside the current screen bounds, resets to center.
    /// </summary>
    WindowSettings? Load();

    /// <summary>
    /// Saves the given window settings to the configuration file.
    /// </summary>
    /// <param name="settings">The window settings to persist.</param>
    void Save(WindowSettings settings);
}

/// <summary>
/// Represents the persisted window size, position, and state.
/// </summary>
/// <param name="Left">The left edge of the window in screen coordinates.</param>
/// <param name="Top">The top edge of the window in screen coordinates.</param>
/// <param name="Width">The width of the window.</param>
/// <param name="Height">The height of the window.</param>
/// <param name="IsMaximized">Whether the window is maximized.</param>
public record WindowSettings(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized
);
