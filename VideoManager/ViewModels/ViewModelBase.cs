using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoManager.ViewModels;

/// <summary>
/// Base class for all ViewModels, providing INotifyPropertyChanged support
/// via CommunityToolkit.Mvvm's ObservableObject.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
}
