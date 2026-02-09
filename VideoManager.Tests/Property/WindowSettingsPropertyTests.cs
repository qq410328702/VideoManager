using System.IO;
using System.Windows;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for window settings round-trip persistence.
/// Tests Property 6: 窗口设置 round-trip
///
/// **Feature: video-manager-optimization, Property 6: 窗口设置 round-trip**
/// **Validates: Requirements 5.1, 5.2**
///
/// For any valid WindowSettings (Left, Top, Width, Height, IsMaximized),
/// saving then loading should return an equivalent WindowSettings object.
/// </summary>
public class WindowSettingsPropertyTests : IDisposable
{
    private const double ScreenWidth = 1920;
    private const double ScreenHeight = 1080;

    private readonly string _tempDir;
    private readonly string _settingsFilePath;
    private readonly Rect _screenBounds = new(0, 0, ScreenWidth, ScreenHeight);

    public WindowSettingsPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wsp-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _settingsFilePath = Path.Combine(_tempDir, "Data", "window-settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private WindowSettingsService CreateService()
    {
        return new WindowSettingsService(_settingsFilePath, () => _screenBounds);
    }

    /// <summary>
    /// Generates a WindowSettings configuration as an int array [left, top, width, height, isMaximized].
    /// Values are constrained so the window is at least partially on screen, ensuring
    /// Load() does not reset the position (round-trip preserves values).
    ///
    /// - Left: 0 to (ScreenWidth - 200), window partially visible horizontally
    /// - Top: 0 to (ScreenHeight - 200), window partially visible vertically
    /// - Width: 200 to ScreenWidth
    /// - Height: 200 to ScreenHeight
    /// - IsMaximized: 0 or 1
    /// </summary>
    private static FsCheck.Arbitrary<int[]> WindowSettingsConfigArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                // Derive 5 values from the random array
                var left = arr.Length > 0 ? arr[0] % ((int)ScreenWidth - 200) : 0;
                var top = arr.Length > 1 ? arr[1] % ((int)ScreenHeight - 200) : 0;
                var width = arr.Length > 2 ? (arr[2] % ((int)ScreenWidth - 200)) + 200 : 800;
                var height = arr.Length > 3 ? (arr[3] % ((int)ScreenHeight - 200)) + 200 : 600;
                var isMaximized = arr.Length > 4 ? arr[4] % 2 : 0;
                return new[] { left, top, width, height, isMaximized };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 5));
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 6: 窗口设置 round-trip**
    /// **Validates: Requirements 5.1, 5.2**
    ///
    /// For any valid WindowSettings where the window is at least partially on screen,
    /// saving then loading should return an equivalent WindowSettings object with
    /// identical Left, Top, Width, Height, and IsMaximized values.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property SaveThenLoadShouldReturnEquivalentSettings()
    {
        return FsCheck.Fluent.Prop.ForAll(WindowSettingsConfigArb(), config =>
        {
            double left = config[0];
            double top = config[1];
            double width = config[2];
            double height = config[3];
            bool isMaximized = config[4] != 0;

            var service = CreateService();
            var original = new WindowSettings(left, top, width, height, isMaximized);

            // Save
            service.Save(original);

            // Load
            var loaded = service.Load();

            // Verify round-trip equivalence
            if (loaded is null)
                return false;

            return Math.Abs(loaded.Left - original.Left) < 0.001
                && Math.Abs(loaded.Top - original.Top) < 0.001
                && Math.Abs(loaded.Width - original.Width) < 0.001
                && Math.Abs(loaded.Height - original.Height) < 0.001
                && loaded.IsMaximized == original.IsMaximized;
        });
    }
}
