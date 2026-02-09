using System.Collections.Generic;
using System.Linq;
using System.Windows;
using VideoManager.Models;

namespace VideoManager.Views;

/// <summary>
/// Dialog for selecting a target category to move multiple videos into in a batch operation.
/// </summary>
public partial class BatchCategoryDialog : Window
{
    /// <summary>
    /// The category selected by the user after confirming.
    /// </summary>
    public FolderCategory? SelectedCategory { get; private set; }

    /// <summary>
    /// Creates a new BatchCategoryDialog.
    /// </summary>
    /// <param name="categories">The list of available categories (tree structure).</param>
    /// <param name="videoCount">The number of videos that will be affected.</param>
    public BatchCategoryDialog(IEnumerable<FolderCategory> categories, int videoCount)
    {
        InitializeComponent();

        InfoText.Text = $"将 {videoCount} 个视频移动到所选分类";
        CategoryTreeView.ItemsSource = categories.ToList();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedCategory = CategoryTreeView.SelectedItem as FolderCategory;

        if (SelectedCategory is null)
        {
            MessageBox.Show("请选择一个目标分类。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
