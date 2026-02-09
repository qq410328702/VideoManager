using System.Windows;
using System.Windows.Controls;
using VideoManager.Models;
using VideoManager.ViewModels;

namespace VideoManager.Views;

/// <summary>
/// Code-behind for CategoryPanel. Kept minimal — only handles the TreeView
/// SelectedItemChanged event since TreeView.SelectedItem is read-only and
/// cannot be bound directly via TwoWay binding in XAML.
/// </summary>
public partial class CategoryPanel : UserControl
{
    public CategoryPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Synchronizes the TreeView's selected item to the ViewModel's SelectedCategory property.
    /// This is necessary because TreeView.SelectedItem is read-only in WPF.
    /// </summary>
    private void CategoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is CategoryViewModel vm)
        {
            vm.SelectedCategory = e.NewValue as FolderCategory;
        }
    }
}
