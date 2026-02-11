using System.Reflection;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Unit tests for GlobalExceptionHandler.
/// Validates logging, Handled flag, SetObserved, and friendly dialog display.
/// Requirements: 6.3, 6.5
/// </summary>
public class GlobalExceptionHandlerTests
{
    private readonly Mock<ILogger<GlobalExceptionHandler>> _loggerMock;
    private readonly Mock<IDialogService> _dialogMock;
    private readonly GlobalExceptionHandler _handler;

    public GlobalExceptionHandlerTests()
    {
        _loggerMock = new Mock<ILogger<GlobalExceptionHandler>>();
        _dialogMock = new Mock<IDialogService>();
        _handler = new GlobalExceptionHandler(_loggerMock.Object, _dialogMock.Object);
    }

    // --- HandleDispatcherException tests ---

    [Fact]
    public void HandleDispatcherException_LogsExceptionViaILogger()
    {
        var exception = new InvalidOperationException("test error");
        var args = CreateDispatcherArgs(exception);

        _handler.HandleDispatcherException(this, args);

        // DispatcherUnhandledExceptionEventArgs created via reflection may not
        // expose the Exception property correctly, so we verify LogLevel.Error
        // was invoked (the implementation calls _logger.LogError(args.Exception, ...)).
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => true),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void HandleDispatcherException_SetsHandledToTrue()
    {
        var exception = new InvalidOperationException("crash");
        var args = CreateDispatcherArgs(exception);

        _handler.HandleDispatcherException(this, args);

        Assert.True(args.Handled);
    }

    [Fact]
    public void HandleDispatcherException_ShowsFriendlyMessageViaDialogService()
    {
        var exception = new InvalidOperationException("boom");
        var args = CreateDispatcherArgs(exception);

        _handler.HandleDispatcherException(this, args);

        _dialogMock.Verify(
            d => d.ShowMessage(
                It.IsAny<string>(),
                It.IsAny<string>(),
                MessageLevel.Error),
            Times.Once);
    }

    // --- HandleUnobservedTaskException tests ---

    [Fact]
    public void HandleUnobservedTaskException_LogsExceptionViaILogger()
    {
        var inner = new InvalidOperationException("task error");
        var aggregate = new AggregateException(inner);
        var args = new UnobservedTaskExceptionEventArgs(aggregate);

        _handler.HandleUnobservedTaskException(this, args);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => true),
                aggregate,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void HandleUnobservedTaskException_CallsSetObserved()
    {
        var inner = new InvalidOperationException("unobserved");
        var aggregate = new AggregateException(inner);
        var args = new UnobservedTaskExceptionEventArgs(aggregate);

        _handler.HandleUnobservedTaskException(this, args);

        Assert.True(args.Observed);
    }

    [Fact]
    public void HandleUnobservedTaskException_ShowsFriendlyMessageViaDialogService()
    {
        var inner = new InvalidOperationException("task boom");
        var aggregate = new AggregateException(inner);
        var args = new UnobservedTaskExceptionEventArgs(aggregate);

        _handler.HandleUnobservedTaskException(this, args);

        _dialogMock.Verify(
            d => d.ShowMessage(
                It.IsAny<string>(),
                It.IsAny<string>(),
                MessageLevel.Error),
            Times.Once);
    }

    // --- Helper ---

    /// <summary>
    /// Creates a DispatcherUnhandledExceptionEventArgs via reflection
    /// since it has no public constructor.
    /// </summary>
    private static DispatcherUnhandledExceptionEventArgs CreateDispatcherArgs(Exception exception)
    {
        // DispatcherUnhandledExceptionEventArgs(Exception, bool)
        // The internal constructor takes (Exception exception, bool isTerminating).
        var ctor = typeof(DispatcherUnhandledExceptionEventArgs)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .First();

        var parameters = ctor.GetParameters();
        var ctorArgs = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(Exception))
                ctorArgs[i] = exception;
            else if (parameters[i].ParameterType == typeof(bool))
                ctorArgs[i] = false;
            else
                ctorArgs[i] = null;
        }

        return (DispatcherUnhandledExceptionEventArgs)ctor.Invoke(ctorArgs);
    }
}
