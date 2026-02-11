using CommunityToolkit.Mvvm.Messaging;
using VideoManager.Models;
using VideoManager.ViewModels;
using Xunit;

namespace VideoManager.Tests.Unit.ViewModels;

[Collection("Messenger")]
public class SortViewModelTests : IDisposable
{
    private readonly SortViewModel _sut;

    public SortViewModelTests()
    {
        // Reset messenger to avoid cross-test interference
        WeakReferenceMessenger.Default.Reset();
        _sut = new SortViewModel();
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.Reset();
    }

    [Fact]
    public void DefaultValues_ShouldBeImportedAtDescending()
    {
        Assert.Equal(SortField.ImportedAt, _sut.CurrentSortField);
        Assert.Equal(SortDirection.Descending, _sut.CurrentSortDirection);
    }

    [Fact]
    public void ToggleSortDirection_FromDescending_ShouldBecomeAscending()
    {
        _sut.ToggleSortDirectionCommand.Execute(null);

        Assert.Equal(SortDirection.Ascending, _sut.CurrentSortDirection);
    }

    [Fact]
    public void ToggleSortDirection_FromAscending_ShouldBecomeDescending()
    {
        _sut.CurrentSortDirection = SortDirection.Ascending;
        WeakReferenceMessenger.Default.Reset();
        WeakReferenceMessenger.Default.Register<SortChangedMessage>(_sut, (_, _) => { });

        _sut.ToggleSortDirectionCommand.Execute(null);

        Assert.Equal(SortDirection.Descending, _sut.CurrentSortDirection);
    }

    [Fact]
    public void ChangingSortField_SendsSortChangedMessage()
    {
        SortChangedMessage? received = null;
        WeakReferenceMessenger.Default.Register<SortChangedMessage>(this, (_, msg) => received = msg);

        _sut.CurrentSortField = SortField.Duration;

        Assert.NotNull(received);
        Assert.Equal(SortField.Duration, received.Field);
        Assert.Equal(SortDirection.Descending, received.Direction);
    }

    [Fact]
    public void ChangingSortDirection_SendsSortChangedMessage()
    {
        SortChangedMessage? received = null;
        WeakReferenceMessenger.Default.Register<SortChangedMessage>(this, (_, msg) => received = msg);

        _sut.CurrentSortDirection = SortDirection.Ascending;

        Assert.NotNull(received);
        Assert.Equal(SortField.ImportedAt, received.Field);
        Assert.Equal(SortDirection.Ascending, received.Direction);
    }

    [Fact]
    public void ToggleSortDirection_SendsSortChangedMessage()
    {
        SortChangedMessage? received = null;
        WeakReferenceMessenger.Default.Register<SortChangedMessage>(this, (_, msg) => received = msg);

        _sut.ToggleSortDirectionCommand.Execute(null);

        Assert.NotNull(received);
        Assert.Equal(SortField.ImportedAt, received.Field);
        Assert.Equal(SortDirection.Ascending, received.Direction);
    }

    [Fact]
    public void SortBy_DifferentField_ChangesSortField()
    {
        SortChangedMessage? received = null;
        WeakReferenceMessenger.Default.Register<SortChangedMessage>(this, (_, msg) => received = msg);

        _sut.SortByCommand.Execute(SortField.FileSize);

        Assert.Equal(SortField.FileSize, _sut.CurrentSortField);
        Assert.NotNull(received);
        Assert.Equal(SortField.FileSize, received.Field);
    }

    [Fact]
    public void SortBy_SameField_TogglesSortDirection()
    {
        // Default is ImportedAt Descending
        SortChangedMessage? received = null;
        WeakReferenceMessenger.Default.Register<SortChangedMessage>(this, (_, msg) => received = msg);

        _sut.SortByCommand.Execute(SortField.ImportedAt);

        // Should toggle direction instead of changing field
        Assert.Equal(SortField.ImportedAt, _sut.CurrentSortField);
        Assert.Equal(SortDirection.Ascending, _sut.CurrentSortDirection);
        Assert.NotNull(received);
        Assert.Equal(SortDirection.Ascending, received.Direction);
    }
}
