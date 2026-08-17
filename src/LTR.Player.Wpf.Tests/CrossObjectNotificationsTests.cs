using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LTR.Player.Wpf;

/// <summary>
/// The forwarding mechanism itself, including the rule that was written out in every handler it replaced.
/// </summary>
/// <remarks>
/// Tested directly rather than only through the shell because one case cannot be reached from there: a
/// wholesale reset, where a property name of <see langword="null"/> or empty means "re-read everything". No
/// section raises one today, so the shell's own tests can only ever cover the named case — and a rule that is
/// only exercised by code nobody has written yet is exactly the kind that quietly stops working.
/// </remarks>
public sealed class CrossObjectNotificationsTests
{
    [Fact]
    public void When_ThePropertyChanges_RaisesTheDependentPropertyAndNotifiesTheCommand()
    {
        // Arrange
        var raised = new List<string>();
        var announcements = 0;
        var command = new RelayCommand(() => { });
        command.CanExecuteChanged += (_, _) => announcements++;

        var source = new Section();
        var notifications = new CrossObjectNotifications(raised.Add);
        notifications.When(source, nameof(Section.Selected)).Raises("Computed").Notifies(command);

        // Act
        source.Raise(nameof(Section.Selected));

        // Assert
        raised.ShouldBe(["Computed"]);
        announcements.ShouldBe(1);
    }

    [Fact]
    public void When_AnotherPropertyChanges_ForwardsNothing()
    {
        // Arrange
        var raised = new List<string>();
        var source = new Section();
        var notifications = new CrossObjectNotifications(raised.Add);
        notifications.When(source, nameof(Section.Selected)).Raises("Computed");

        // Act
        source.Raise(nameof(Section.Unrelated));

        // Assert
        raised.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void When_EverythingChangesAtOnce_ForwardsAllOfIt(string? propertyName)
    {
        // Arrange: the rule this class exists to state once. Both WPF and the toolkit use an empty or null
        // name to mean "re-read everything", and a forward that compares names without allowing for it drops
        // every wholesale reset silently.
        var raised = new List<string>();
        var announcements = 0;
        var command = new RelayCommand(() => { });
        command.CanExecuteChanged += (_, _) => announcements++;

        var source = new Section();
        var notifications = new CrossObjectNotifications(raised.Add);
        notifications.When(source, nameof(Section.Selected)).Raises("Computed").Notifies(command);

        // Act
        source.Raise(propertyName);

        // Assert
        raised.ShouldBe(["Computed"]);
        announcements.ShouldBe(1);
    }

    [Fact]
    public void When_TwoPropertiesOfOneSourceAreForwarded_EachGoesToItsOwnDependents()
    {
        // Arrange: one subscription serves both, so a mistake in the dispatch would cross them.
        var raised = new List<string>();
        var source = new Section();
        var notifications = new CrossObjectNotifications(raised.Add);
        notifications.When(source, nameof(Section.Selected)).Raises("FromSelected");
        notifications.When(source, nameof(Section.Unrelated)).Raises("FromUnrelated");

        // Act
        source.Raise(nameof(Section.Unrelated));

        // Assert
        raised.ShouldBe(["FromUnrelated"]);
    }

    [Fact]
    public void When_TwoSourcesShareAPropertyName_OnlyTheOneThatChangedIsForwarded()
    {
        // Arrange: the sections do share names — SearchText and SelectedCategory exist on both catalogue
        // sections — so registrations are kept per source object and by reference.
        var raised = new List<string>();
        var films = new Section();
        var series = new Section();

        var notifications = new CrossObjectNotifications(raised.Add);
        notifications.When(films, nameof(Section.Selected)).Raises("FromFilms");
        notifications.When(series, nameof(Section.Selected)).Raises("FromSeries");

        // Act
        series.Raise(nameof(Section.Selected));

        // Assert
        raised.ShouldBe(["FromSeries"]);
    }

    /// <summary>
    /// A stand-in for a section, raising changes on demand.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than one of the real view models: what is under test is the forwarding, and a real
    /// section would bring a store, a clock and a detail service with it to raise one event.
    /// </remarks>
    private sealed class Section : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public object? Selected { get; set; }

        public object? Unrelated { get; set; }

        public void Raise(string? propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
