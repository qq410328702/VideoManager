using System.Windows;
using VideoManager.ViewModels;

namespace VideoManager.Views;

/// <summary>
/// Code-behind for ImportDialog. Kept minimal — only folder browser dialog
/// handlers that cannot be expressed purely in XAML.
/// </summary>
public partial class ImportDialog : Window
{
    public ImportDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Convenience constructor that sets the DataContext to the provided ViewModel.
    /// </summary>
    /// <param name="viewModel">The ImportViewModel to bind to.</param>
    public ImportDialog(ImportViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.ImportCompleted += () =>
        {
            DialogResult = true;
            Close();
        };
    }

    /// <summary>
    /// Opens a folder browser dialog for selecting the source folder to scan.
    /// </summary>
    private void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var folderPath = ShowFolderBrowserDialog("选择要扫描的源文件夹");
        if (!string.IsNullOrEmpty(folderPath) && DataContext is ImportViewModel vm)
        {
            vm.SelectedFolderPath = folderPath;
        }
    }

    /// <summary>
    /// Opens a folder browser dialog for selecting the library target directory.
    /// </summary>
    private void BrowseLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        var folderPath = ShowFolderBrowserDialog("选择视频库目标目录");
        if (!string.IsNullOrEmpty(folderPath) && DataContext is ImportViewModel vm)
        {
            vm.LibraryPath = folderPath;
        }
    }

    /// <summary>
    /// Handles the Cancel/Close button click. If an operation is in progress,
    /// triggers cancellation via the ViewModel command; otherwise closes the dialog.
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ImportViewModel vm)
        {
            if (vm.CancelCommand.CanExecute(null))
            {
                vm.CancelCommand.Execute(null);
                return;
            }
        }

        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Shows a folder browser dialog using Microsoft.Win32.OpenFolderDialog (.NET 8+).
    /// Falls back gracefully if the dialog is not available.
    /// </summary>
    /// <param name="title">The dialog title/description.</param>
    /// <returns>The selected folder path, or null if cancelled.</returns>
    private string? ShowFolderBrowserDialog(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        var result = dialog.ShowDialog(this);
        return result == true ? dialog.FolderName : null;
    }
}
