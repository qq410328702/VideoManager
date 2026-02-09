using System.Collections.Generic;
using System.Linq;
using System.Windows;
using VideoManager.Models;

namespace VideoManager.Views;

/// <summary>
/// Dialog for selecting tags to add to multiple videos in a batch operation.
/// </summary>
public partial class BatchTagDialog : Window
{
    /// <summary>
    /// The tags selected by the user after confirming.
    /// </summary>
    public List<Tag> SelectedTags { get; private set; } = new();

    /// <summary>
    /// Creates a new BatchTagDialog.
    /// </summary>
    /// <param name="availableTags">The list of available tags to choose from.</param>
    /// <param name="videoCount">The number of videos that will be affected.</param>
    public BatchTagDialog(IEnumerable<Tag> availableTags, int videoCount)
    {
        InitializeComponent();

        InfoText.Text = $"将为 {videoCount} 个视频添加所选标签";
        TagListBox.ItemsSource = availableTags.ToList();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedTags = TagListBox.SelectedItems.Cast<Tag>().ToList();

        if (SelectedTags.Count == 0)
        {
            MessageBox.Show("请至少选择一个标签。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
