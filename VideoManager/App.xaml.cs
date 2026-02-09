using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;
using VideoManager.ViewModels;

namespace VideoManager;

/// <summary>
/// Interaction logic for App.xaml.
/// Configures dependency injection and application startup.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The application-wide service provider for resolving dependencies.
    /// </summary>
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers to catch unhandled crashes
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"未处理的 UI 异常:\n\n{args.Exception}",
                "应用错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"未处理的异常:\n\n{ex}",
                    "致命错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            MessageBox.Show(
                $"未观察的任务异常:\n\n{args.Exception}",
                "任务错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.SetObserved();
        };

        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        // Ensure database is created and up-to-date
        using (var scope = ServiceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<VideoManagerDbContext>();
            try
            {
                dbContext.Database.Migrate();
            }
            catch
            {
                // Database was created with EnsureCreated() (no migration history).
                // Manually apply missing schema changes.
                dbContext.Database.EnsureCreated();
                try
                {
                    dbContext.Database.ExecuteSqlRaw("ALTER TABLE Tags ADD COLUMN Color TEXT NULL");
                }
                catch { /* column already exists */ }
                try
                {
                    dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_VideoEntries_FileSize ON VideoEntries (FileSize)");
                }
                catch { /* index already exists */ }
            }
        }

        // Check FFmpeg availability
        var ffmpegService = ServiceProvider.GetRequiredService<IFFmpegService>();
        var ffmpegAvailable = await ffmpegService.CheckAvailabilityAsync();
        if (!ffmpegAvailable)
        {
            MessageBox.Show(
                "FFmpeg was not found on this system.\n\n" +
                "Video metadata extraction and thumbnail generation will not be available.\n" +
                "Please install FFmpeg and ensure it is in your system PATH to enable full functionality.",
                "FFmpeg Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    /// <summary>
    /// Configures all services, repositories, ViewModels, and the DbContext
    /// in the dependency injection container.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // --- Database ---
        var appDir = AppContext.BaseDirectory;
        var dataDir = Path.Combine(appDir, "Data");
        Directory.CreateDirectory(dataDir);

        var dbPath = Path.Combine(dataDir, "videomanager.db");

        services.AddDbContext<VideoManagerDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"),
            ServiceLifetime.Scoped);

        // --- Repositories (Singleton – they receive a scoped DbContext via scope) ---
        services.AddScoped<IVideoRepository, VideoRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();

        // --- Services (Singleton) ---
        services.AddSingleton<IFFmpegService, FFmpegService>();

        // Configure VideoManagerOptions with library/thumbnail paths
        var videoLibraryPath = Path.Combine(appDir, "VideoLibrary");
        var thumbnailDir = Path.Combine(appDir, "Thumbnails");
        Directory.CreateDirectory(videoLibraryPath);
        Directory.CreateDirectory(thumbnailDir);

        services.Configure<VideoManagerOptions>(opts =>
        {
            opts.VideoLibraryPath = videoLibraryPath;
            opts.ThumbnailDirectory = thumbnailDir;
        });

        services.AddScoped<IImportService, ImportService>();

        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IEditService, EditService>();
        services.AddScoped<IDeleteService, DeleteService>();
        services.AddSingleton<IWindowSettingsService, WindowSettingsService>();
        services.AddSingleton<IThumbnailCacheService, ThumbnailCacheService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();

        // --- ViewModels (Transient – new instance per request) ---
        services.AddTransient<VideoListViewModel>(sp =>
        {
            var videoRepository = sp.GetRequiredService<IVideoRepository>();
            var thumbnailCacheService = sp.GetRequiredService<IThumbnailCacheService>();
            return new VideoListViewModel(videoRepository, thumbnailCacheService.LoadThumbnailAsync);
        });
        services.AddTransient<ImportViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<CategoryViewModel>();
        services.AddTransient<EditViewModel>();
        services.AddTransient<VideoPlayerViewModel>();
        services.AddTransient<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<VideoListViewModel>(),
            sp.GetRequiredService<SearchViewModel>(),
            sp.GetRequiredService<CategoryViewModel>(),
            sp.GetRequiredService<IFileWatcherService>(),
            sp.GetRequiredService<IOptions<VideoManagerOptions>>()));

        // --- Views ---
        services.AddTransient<MainWindow>(sp => new MainWindow(
            sp.GetRequiredService<MainViewModel>(),
            sp.GetRequiredService<IWindowSettingsService>()));
    }
}
