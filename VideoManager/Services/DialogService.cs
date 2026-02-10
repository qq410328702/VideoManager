using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VideoManager.Models;
using VideoManager.ViewModels;
using VideoManager.Views;

namespace VideoManager.Services;

/// <summary>
/// Implements <see cref="IDialogService"/> by creating WPF dialogs on the UI thread.
/// Uses <see cref="Application.Current.Dispatcher"/> to ensure all dialog operations
/// happen on the UI thread, and sets <c>Owner = Application.Current.MainWindow</c>
/// on all dialogs for proper modal behavior.
/// </summary>
public class DialogService : IDialogService
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Creates a new DialogService.
    /// </summary>
    /// <param name="serviceProvider">The DI service provider for resolving ViewModels.</param>
    public DialogService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public async Task<bool> ShowEditDialogAsync(VideoEntry video)
    {
        ArgumentNullException.ThrowIfNull(video);

        var saved = false;

        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var editVm = scope.ServiceProvider.GetRequiredService<EditViewModel>();
            await editVm.LoadVideoAsync(video);

            var dialog = new EditDialog(editVm)
            {
                Owner = Application.Current.MainWindow
            };

            saved = dialog.ShowDialog() == true;
        });

        return saved;
    }

    /// <inheritdoc />
    public async Task<(bool Confirmed, bool DeleteFile)?> ShowDeleteConfirmAsync(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        (bool Confirmed, bool DeleteFile)? result = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new ConfirmDeleteDialog(title)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true && dialog.DeleteFile is not null)
            {
                result = (true, dialog.DeleteFile.Value);
            }
        });

        return result;
    }

    /// <inheritdoc />
    public async Task<(bool Confirmed, bool DeleteFile)?> ShowBatchDeleteConfirmAsync(int count)
    {
        (bool Confirmed, bool DeleteFile)? result = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new ConfirmDeleteDialog(count)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true && dialog.DeleteFile is not null)
            {
                result = (true, dialog.DeleteFile.Value);
            }
        });

        return result;
    }

    /// <inheritdoc />
    public async Task<List<Tag>?> ShowBatchTagDialogAsync(IEnumerable<Tag> availableTags, int selectedCount)
    {
        ArgumentNullException.ThrowIfNull(availableTags);

        List<Tag>? result = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new BatchTagDialog(availableTags, selectedCount)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                result = dialog.SelectedTags;
            }
        });

        return result;
    }

    /// <inheritdoc />
    public async Task<FolderCategory?> ShowBatchCategoryDialogAsync(IEnumerable<FolderCategory> categories, int selectedCount)
    {
        ArgumentNullException.ThrowIfNull(categories);

        FolderCategory? result = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new BatchCategoryDialog(categories, selectedCount)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                result = dialog.SelectedCategory;
            }
        });

        return result;
    }

    /// <inheritdoc />
    public void ShowMessage(string message, string title, MessageLevel level = MessageLevel.Information)
    {
        var image = MapMessageLevel(level);

        if (Application.Current.Dispatcher.CheckAccess())
        {
            MessageBox.Show(
                Application.Current.MainWindow!,
                message,
                title,
                MessageBoxButton.OK,
                image);
        }
        else
        {
            Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show(
                    Application.Current.MainWindow!,
                    message,
                    title,
                    MessageBoxButton.OK,
                    image));
        }
    }

    /// <inheritdoc />
    public async Task<bool> ShowConfirmAsync(string message, string title)
    {
        var confirmed = false;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var result = MessageBox.Show(
                Application.Current.MainWindow!,
                message,
                title,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            confirmed = result == MessageBoxResult.OK;
        });

        return confirmed;
    }

    /// <summary>
    /// Maps a <see cref="MessageLevel"/> to the corresponding <see cref="MessageBoxImage"/>.
    /// </summary>
    private static MessageBoxImage MapMessageLevel(MessageLevel level) => level switch
    {
        MessageLevel.Information => MessageBoxImage.Information,
        MessageLevel.Warning => MessageBoxImage.Warning,
        MessageLevel.Error => MessageBoxImage.Error,
        _ => MessageBoxImage.Information
    };
}
