using System.Windows.Controls;

namespace LTR.Player.Wpf.Views;

/// <summary>
/// The category picker and its pin, shared by the three sections that have one.
/// </summary>
/// <remarks>
/// Bound to the <see cref="CategoryPickerViewModel"/> each section holds rather than to a named section — see
/// the markup. It holds no code because there is nothing here that is not a binding.
/// </remarks>
public partial class CategoryPickerView : UserControl
{
    public CategoryPickerView()
    {
        InitializeComponent();
    }
}
