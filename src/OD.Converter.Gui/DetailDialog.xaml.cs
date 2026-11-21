using System.Runtime.InteropServices;
using System.Windows;

namespace OD.Converter.Gui;

/// <summary>Displays selectable details and uses cancellation as the safe default for decisions.</summary>
public partial class DetailDialog : DarkWindow
{
    public DetailDialog(Window owner, string heading, string text, string? accept = null)
    {
        InitializeComponent();
        Owner = owner;
        Title = heading;
        Heading.Text = heading;
        Details.Text = text;
        AcceptButton.Content = accept ?? "_Close";
        AcceptButton.IsDefault = accept is null;
        CancelButton.Visibility = accept is null ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) =>
        {
            if (accept is null) AcceptButton.Focus();
            else CancelButton.Focus();
        };
    }

    private void AcceptClicked(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Details.Text);
            CopyButton.Content = "Copied";
        }
        catch (ExternalException)
        {
            CopyButton.Content = "Copy failed. Retry";
        }
    }
}
