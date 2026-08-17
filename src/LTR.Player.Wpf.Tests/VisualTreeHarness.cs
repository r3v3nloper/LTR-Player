using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace LTR.Player.Wpf;

/// <summary>
/// What a test needs before it can build a piece of the real window: an apartment and the theme.
/// </summary>
/// <remarks>
/// Shared by the two test classes that measure markup rather than logic. Neither could exist without both
/// halves — xunit provides no single-threaded apartment, and a view built without the theme fails every
/// styled binding in it while still looking like a tree.
/// </remarks>
internal static class VisualTreeHarness
{
    /// <summary>
    /// Guards the one thing here that an application may only do once, whoever gets there first.
    /// </summary>
    /// <remarks>
    /// Each test builds its tree on a thread of its own and xunit runs the classes that do so at the same
    /// time, so without this two of them construct an <see cref="Application"/> apiece — which the second
    /// one throws on, taking a test with it that had nothing to do with the collision.
    /// </remarks>
    private static readonly Lock ThemeGate = new();

    /// <summary>
    /// Runs <paramref name="work"/> on a thread WPF will accept, and brings back what it returned.
    /// </summary>
    /// <remarks>
    /// A thread per test rather than a shared one: each builds its own visual tree, and a dispatcher left
    /// running from a previous test is how these become order-dependent.
    /// </remarks>
    public static T OnStaThread<T>(Func<T> work)
    {
        var result = default(T);
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureThemeLoaded();
                result = work();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("The visual tree could not be built.", failure);
        }

        return result!;
    }

    /// <summary>
    /// Makes the theme's brushes, styles and converters resolvable, as the running application does.
    /// </summary>
    public static void EnsureThemeLoaded()
    {
        lock (ThemeGate)
        {
            if (Application.Current is null)
            {
                _ = new Application();
            }

            if (Application.Current!.Resources.Contains("Negated"))
            {
                return;
            }

            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/LTR-Player;component/Theme.xaml", UriKind.Relative),
            });
        }
    }

    /// <summary>The one descendant of <paramref name="root"/> carrying <paramref name="name"/>.</summary>
    public static T Descendant<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        return Descendants<T>(root).First(element => element.Name == name);
    }

    /// <summary>Every descendant of <paramref name="root"/> of the given type, outermost first.</summary>
    public static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// Lets the work WPF has queued for later run now.
    /// </summary>
    /// <remarks>
    /// A test has no message loop, so anything the framework posts to the dispatcher — <c>Loaded</c> above
    /// all — would never arrive. Draining at that priority is what makes a shown window behave here as it
    /// does in the application.
    /// </remarks>
    public static void PumpDispatcher(DispatcherObject element)
    {
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
    }
}
