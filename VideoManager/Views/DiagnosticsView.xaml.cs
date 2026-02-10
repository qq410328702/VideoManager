using System.Windows.Controls;

namespace VideoManager.Views;

/// <summary>
/// Code-behind for DiagnosticsView. Kept minimal — only calls InitializeComponent.
/// All logic is handled by <see cref="VideoManager.ViewModels.DiagnosticsViewModel"/>.
/// </summary>
public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView()
    {
        InitializeComponent();
    }
}
