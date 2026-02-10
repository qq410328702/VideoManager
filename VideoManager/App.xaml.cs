using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// Timer for periodic database backups.
    /// </summary>
    private System.Threading.Timer? _periodicBackupTimer;

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
                try
                {
                    dbContext.Database.ExecuteSqlRaw("ALTER TABLE VideoEntries ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0");
                }
                catch { /* column already exists */ }
                try
                {
                    dbContext.Database.ExecuteSqlRaw("ALTER TABLE VideoEntries ADD COLUMN DeletedAt TEXT NULL");
                }
                catch { /* column already exists */ }
            }

            // Configure SQLite WAL mode and performance pragmas
            dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            dbContext.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
            dbContext.Database.ExecuteSqlRaw("PRAGMA cache_size=-8000;");
        }

        // --- Database Backup & Integrity Check (Req 8.1, 9.1, 9.2, 9.3) ---
        await PerformStartupBackupAsync();

        // --- Start Periodic Backup Timer (Req 8.2) ---
        StartPeriodicBackupTimer();

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
    /// Performs startup database integrity check and backup.
    /// If integrity check fails, attempts to restore from the latest backup.
    /// If no backup is available, shows a warning to the user.
    /// (Req 8.1, 9.1, 9.2, 9.3)
    /// </summary>
    private static async Task PerformStartupBackupAsync()
    {
        var logger = ServiceProvider.GetRequiredService<ILogger<App>>();
        var backupService = ServiceProvider.GetRequiredService<IBackupService>();

        try
        {
            // Step 1: Check database integrity (Req 9.1)
            var isIntact = await backupService.CheckIntegrityAsync(CancellationToken.None);

            if (!isIntact)
            {
                // Integrity check failed - attempt restore from latest backup (Req 9.2)
                logger.LogError("Database integrity check failed at startup. Attempting restore from latest backup.");

                var backups = backupService.ListBackups();
                if (backups.Count > 0)
                {
                    try
                    {
                        await backupService.RestoreFromBackupAsync(backups[0].FilePath, CancellationToken.None);
                        logger.LogInformation("Database restored from backup: {BackupPath}", backups[0].FilePath);
                    }
                    catch (Exception restoreEx)
                    {
                        logger.LogError(restoreEx, "Failed to restore database from backup");
                        MessageBox.Show(
                            "数据库完整性检查失败，且从备份恢复也失败。\n应用将继续运行，但数据可能不完整。",
                            "数据库警告",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                else
                {
                    // No backup available (Req 9.3)
                    logger.LogCritical("Database integrity check failed and no backup files are available for restore");
                    MessageBox.Show(
                        "数据库完整性检查失败，且没有可用的备份文件。\n应用将继续运行，但数据可能已损坏。\n建议尽快手动备份重要数据。",
                        "严重警告",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            // Step 2: Create startup backup (Req 8.1)
            try
            {
                await backupService.CreateBackupAsync(CancellationToken.None);
                logger.LogInformation("Startup backup created successfully");

                // Clean up old backups after creating a new one
                await backupService.CleanupOldBackupsAsync(CancellationToken.None);
            }
            catch (Exception backupEx)
            {
                logger.LogError(backupEx, "Failed to create startup backup. Continuing without backup.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during startup backup process. Continuing without backup.");
        }
    }

    /// <summary>
    /// Starts a periodic timer that creates database backups at the configured interval.
    /// (Req 8.2)
    /// </summary>
    private void StartPeriodicBackupTimer()
    {
        var logger = ServiceProvider.GetRequiredService<ILogger<App>>();
        var options = ServiceProvider.GetRequiredService<IOptions<VideoManagerOptions>>();
        var intervalHours = options.Value.BackupIntervalHours;

        if (intervalHours <= 0)
        {
            logger.LogInformation("Periodic backup is disabled (BackupIntervalHours = {Interval})", intervalHours);
            return;
        }

        var interval = TimeSpan.FromHours(intervalHours);

        _periodicBackupTimer = new System.Threading.Timer(
            async _ => await PeriodicBackupCallbackAsync(),
            null,
            interval,   // First execution after one interval
            interval);  // Then repeat at the same interval

        logger.LogInformation("Periodic backup timer started with interval of {IntervalHours} hours", intervalHours);
    }

    /// <summary>
    /// Callback for the periodic backup timer. Creates a backup and cleans up old backups.
    /// Errors are logged but do not crash the application.
    /// </summary>
    private static async Task PeriodicBackupCallbackAsync()
    {
        var logger = ServiceProvider.GetRequiredService<ILogger<App>>();

        try
        {
            var backupService = ServiceProvider.GetRequiredService<IBackupService>();

            logger.LogInformation("Periodic backup starting");
            await backupService.CreateBackupAsync(CancellationToken.None);
            await backupService.CleanupOldBackupsAsync(CancellationToken.None);
            logger.LogInformation("Periodic backup completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Periodic backup failed");
        }
    }

    /// <summary>
    /// Disposes the periodic backup timer when the application exits.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _periodicBackupTimer?.Dispose();
        _periodicBackupTimer = null;
        base.OnExit(e);
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
            options.UseSqlite($"Data Source={dbPath};Cache=Shared"),
            ServiceLifetime.Scoped);

        // Register IDbContextFactory for services that need to create their own DbContext instances
        services.AddDbContextFactory<VideoManagerDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath};Cache=Shared"),
            ServiceLifetime.Singleton);

        // --- Logging ---
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // --- Repositories (Singleton – they receive a scoped DbContext via scope) ---
        services.AddScoped<IVideoRepository, VideoRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();

        // --- Services (Singleton) ---
        services.AddSingleton<IFFmpegService>(sp =>
            new FFmpegService(sp.GetRequiredService<IMetricsService>()));

        // Configure VideoManagerOptions with library/thumbnail paths
        var videoLibraryPath = Path.Combine(appDir, "VideoLibrary");
        var thumbnailDir = Path.Combine(appDir, "Thumbnails");
        Directory.CreateDirectory(videoLibraryPath);
        Directory.CreateDirectory(thumbnailDir);

        var backupDir = Path.Combine(appDir, "Backups");
        Directory.CreateDirectory(backupDir);

        services.Configure<VideoManagerOptions>(opts =>
        {
            opts.VideoLibraryPath = videoLibraryPath;
            opts.ThumbnailDirectory = thumbnailDir;
            opts.ThumbnailCacheMaxSize = 1000;
            opts.BackupDirectory = backupDir;
        });

        services.AddScoped<IImportService, ImportService>();

        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IEditService, EditService>();
        services.AddScoped<IDeleteService, DeleteService>();
        services.AddSingleton<IWindowSettingsService, WindowSettingsService>();
        services.AddSingleton<IThumbnailCacheService, ThumbnailCacheService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();

        // --- MetricsService (Singleton) ---
        services.AddSingleton<IMetricsService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<MetricsService>>();
            var thumbnailCacheService = sp.GetRequiredService<IThumbnailCacheService>();
            var options = sp.GetRequiredService<IOptions<VideoManagerOptions>>();
            var metricsService = new MetricsService(logger, thumbnailCacheService);
            metricsService.MemoryWarningThresholdBytes = options.Value.MemoryWarningThresholdMb * 1024 * 1024;
            return metricsService;
        });

        // --- BackupService (Singleton) ---
        services.AddSingleton<IBackupService, BackupService>();

        // --- NavigationService & DialogService (Singleton) ---
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();

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
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<VideoListViewModel>(),
            sp.GetRequiredService<SearchViewModel>(),
            sp.GetRequiredService<CategoryViewModel>(),
            sp.GetRequiredService<IFileWatcherService>(),
            sp.GetRequiredService<IOptions<VideoManagerOptions>>(),
            sp.GetRequiredService<INavigationService>(),
            sp.GetRequiredService<IDialogService>(),
            sp));

        // --- Views ---
        services.AddTransient<MainWindow>(sp => new MainWindow(
            sp.GetRequiredService<MainViewModel>(),
            sp.GetRequiredService<IWindowSettingsService>()));
    }
}
