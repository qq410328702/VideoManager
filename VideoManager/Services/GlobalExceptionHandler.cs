using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace VideoManager.Services;

/// <summary>
/// Global exception handler that catches unhandled exceptions on the UI thread
/// and unobserved task exceptions, logs full details via ILogger, and shows
/// a friendly message to the user via IDialogService.
/// </summary>
public class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IDialogService _dialogService;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IDialogService dialogService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    }

    /// <summary>
    /// Registers DispatcherUnhandledException and TaskScheduler.UnobservedTaskException handlers.
    /// </summary>
    public void Register(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.DispatcherUnhandledException += HandleDispatcherException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
    }

    /// <summary>
    /// Handles UI thread unhandled exceptions.
    /// Logs the full exception and shows a friendly message. Sets Handled = true to prevent crash.
    /// </summary>
    internal void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        _logger.LogError(args.Exception, "未处理的 UI 线程异常");

        args.Handled = true;

        ShowFriendlyError();
    }

    /// <summary>
    /// Handles unobserved task exceptions.
    /// Logs the full exception, calls SetObserved(), and shows a friendly message.
    /// </summary>
    internal void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        _logger.LogError(args.Exception, "未观察的任务异常");

        args.SetObserved();

        ShowFriendlyError();
    }

    /// <summary>
    /// Shows a friendly error message via IDialogService.
    /// Falls back to MessageBox.Show if the dialog service itself throws.
    /// </summary>
    private void ShowFriendlyError()
    {
        try
        {
            _dialogService.ShowMessage(
                "操作过程中发生了意外错误，请稍后重试。如果问题持续存在，请联系技术支持。",
                "应用错误",
                MessageLevel.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DialogService 展示错误提示时发生异常，回退到 MessageBox");

            try
            {
                MessageBox.Show(
                    "操作过程中发生了意外错误，请稍后重试。",
                    "应用错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // Last resort — nothing more we can do
            }
        }
    }
}
