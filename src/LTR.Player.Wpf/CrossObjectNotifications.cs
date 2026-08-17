using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LTR.Player.Wpf;

/// <summary>
/// Carries a change notification from the object that owns a property to the object that depends on it.
/// </summary>
/// <remarks>
/// <para>
/// <c>[NotifyCanExecuteChangedFor]</c> cannot cross an object boundary: a command lives on the shell and the
/// property its guard reads belongs to a section, so the shell has to subscribe and forward. That was eight
/// handlers of hand-written plumbing, each repeating the one rule that is easy to get wrong — <b>an empty or
/// null property name means every property</b>, which both WPF and the toolkit use to mean "re-read
/// everything". Omitting that check silently drops every wholesale reset; it lives here once instead.
/// </para>
/// <para>
/// Only *forwarding* belongs here. A change that starts work, or reveals the overlay, is a reaction rather
/// than a notification and stays with the shell, which owns the lifetime token those need. Mixing the two was
/// what made the original block look like ninety lines of plumbing when part of it was behaviour.
/// </para>
/// </remarks>
internal sealed class CrossObjectNotifications
{
    private readonly Action<string> _raiseOnOwner;

    /// <summary>
    /// Registrations per source object, keyed by reference so two equal-looking view models stay distinct.
    /// </summary>
    private readonly Dictionary<INotifyPropertyChanged, List<Forward>> _bySource =
        new(ReferenceEqualityComparer.Instance);

    /// <param name="raiseOnOwner">
    /// Raises a property change on the object doing the depending — the shell's own
    /// <c>OnPropertyChanged</c>.
    /// </param>
    public CrossObjectNotifications(Action<string> raiseOnOwner)
    {
        _raiseOnOwner = raiseOnOwner;
    }

    /// <summary>
    /// Begins a forward from one property of <paramref name="source"/>.
    /// </summary>
    /// <remarks>
    /// Subscription order is preserved across all sources, and matters where the shell also reacts to the
    /// same object: registering the forwards first keeps a command notified before the work its guard may
    /// depend on begins, which is the order the hand-written handlers had.
    /// </remarks>
    public Forward When(INotifyPropertyChanged source, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(propertyName);

        if (!_bySource.TryGetValue(source, out var forwards))
        {
            forwards = [];
            _bySource[source] = forwards;

            // Captured rather than looked up again, so the handler cannot see registrations for a different
            // source and does no work per change beyond the list it owns.
            source.PropertyChanged += (_, e) => Dispatch(forwards, e.PropertyName);
        }

        var forward = new Forward(propertyName);
        forwards.Add(forward);

        return forward;
    }

    private void Dispatch(List<Forward> forwards, string? changedPropertyName)
    {
        // The rule this class exists to state once: an empty or null name means every property.
        var everything = string.IsNullOrEmpty(changedPropertyName);

        foreach (var forward in forwards)
        {
            if (everything || forward.Matches(changedPropertyName))
            {
                forward.Apply(_raiseOnOwner);
            }
        }
    }

    /// <summary>What one property change should announce elsewhere.</summary>
    internal sealed class Forward
    {
        private readonly string _propertyName;
        private readonly List<string> _ownerProperties = [];
        private readonly List<IRelayCommand> _commands = [];

        internal Forward(string propertyName)
        {
            _propertyName = propertyName;
        }

        /// <summary>Raises a property change on the depending object, for something computed from this.</summary>
        public Forward Raises(string ownerPropertyName)
        {
            _ownerProperties.Add(ownerPropertyName);
            return this;
        }

        /// <summary>Tells a command its guard may now answer differently.</summary>
        public Forward Notifies(IRelayCommand command)
        {
            _commands.Add(command);
            return this;
        }

        internal bool Matches(string? changedPropertyName)
        {
            return string.Equals(_propertyName, changedPropertyName, StringComparison.Ordinal);
        }

        internal void Apply(Action<string> raiseOnOwner)
        {
            foreach (var ownerProperty in _ownerProperties)
            {
                raiseOnOwner(ownerProperty);
            }

            foreach (var command in _commands)
            {
                command.NotifyCanExecuteChanged();
            }
        }
    }
}
