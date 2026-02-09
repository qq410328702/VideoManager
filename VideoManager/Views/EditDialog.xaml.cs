using System.Windows;
using VideoManager.ViewModels;

namespace VideoManager.Views;

/// <summary>
/// Code-behind for EditDialog. Kept minimal — only the close button handler
/// that cannot be expressed purely in XAML.
/// </summary>
public partial class EditDialog : Window
{
    public EditDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Convenience constructor that sets the DataContext to the provided ViewModel.
    /// </summary>
    /// <param name="viewModel">The EditViewModel to bind to.</param>
    public EditDialog(EditViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.SaveCompleted += () =>
        {
            DialogResult = true;
            Close();
        };
    }

    /// <summary>
    /// Handles the Close button click. Closes the dialog.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
