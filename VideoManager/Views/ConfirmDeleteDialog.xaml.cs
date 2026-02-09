using System.Windows;

namespace VideoManager.Views;

public partial class ConfirmDeleteDialog : Window
{
    /// <summary>
    /// null = cancelled, false = remove from library only, true = also delete source file
    /// </summary>
    public bool? DeleteFile { get; private set; }

    public ConfirmDeleteDialog(string videoTitle)
    {
        InitializeComponent();
        MessageText.Text = $"确定要删除视频 \"{videoTitle}\" 吗？";
    }

    public ConfirmDeleteDialog(int count)
    {
        InitializeComponent();
        MessageText.Text = $"确定要删除选中的 {count} 个视频吗？";
    }

    private void RemoveFromLibrary_Click(object sender, RoutedEventArgs e)
    {
        DeleteFile = false;
        DialogResult = true;
        Close();
    }

    private void DeleteFile_Click(object sender, RoutedEventArgs e)
    {
        DeleteFile = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DeleteFile = null;
        DialogResult = false;
        Close();
    }
}
