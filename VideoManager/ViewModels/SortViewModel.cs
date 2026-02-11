using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using VideoManager.Models;

namespace VideoManager.ViewModels;

/// <summary>
/// ViewModel responsible for sorting logic, extracted from MainViewModel.
/// Manages current sort field, sort direction, and toggle/sort-by commands.
/// Communicates sort changes via WeakReferenceMessenger.
/// </summary>
public partial class SortViewModel : ViewModelBase
{
    /// <summary>
    /// Current sort field for the video list.
    /// Changing this sends a SortChangedMessage via Messenger.
    /// </summary>
    [ObservableProperty]
    private SortField _currentSortField = SortField.ImportedAt;

    /// <summary>
    /// Current sort direction for the video list.
    /// Changing this sends a SortChangedMessage via Messenger.
    /// </summary>
    [ObservableProperty]
    private SortDirection _currentSortDirection = SortDirection.Descending;

    /// <summary>
    /// Toggles the sort direction between Ascending and Descending.
    /// </summary>
    [RelayCommand]
    private void ToggleSortDirection()
    {
        CurrentSortDirection = CurrentSortDirection == SortDirection.Ascending
            ? SortDirection.Descending
            : SortDirection.Ascending;
    }

    /// <summary>
    /// Sets the sort field to the specified value.
    /// If the field is already selected, toggles the sort direction instead.
    /// </summary>
    /// <param name="field">The sort field to apply.</param>
    [RelayCommand]
    private void SortBy(SortField field)
    {
        if (CurrentSortField == field)
        {
            ToggleSortDirection();
        }
        else
        {
            CurrentSortField = field;
        }
    }

    partial void OnCurrentSortFieldChanged(SortField value)
    {
        SendSortChangedMessage();
    }

    partial void OnCurrentSortDirectionChanged(SortDirection value)
    {
        SendSortChangedMessage();
    }

    private void SendSortChangedMessage()
    {
        WeakReferenceMessenger.Default.Send(
            new SortChangedMessage(CurrentSortField, CurrentSortDirection));
    }
}
