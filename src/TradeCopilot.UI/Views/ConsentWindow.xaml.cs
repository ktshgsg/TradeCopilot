using System.IO;
using System.Windows;
using TradeCopilot.UI.Logic;

namespace TradeCopilot.UI.Views;

/// <summary>
/// First-run consent dialog. Shown modally by <see cref="App"/> before the overlay when the
/// flag file is missing; sets <see cref="Accepted"/> only after "I agree" writes the flag.
/// Closing the dialog (Quit or X) leaves <see cref="Accepted"/> false so App shuts down
/// without recording consent.
/// </summary>
public partial class ConsentWindow : Window
{
    private readonly string _flagPath;

    /// <summary>Creates the dialog.</summary>
    /// <param name="flagPath">Consent flag file path (usually <see cref="ConsentGate.DefaultFlagPath"/>).</param>
    public ConsentWindow(string flagPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flagPath);
        _flagPath = flagPath;
        InitializeComponent();
    }

    /// <summary>True when the user clicked "I agree" and the flag file was written.</summary>
    public bool Accepted { get; private set; }

    private void AgreeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConsentGate.MarkAccepted(_flagPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                $"Could not save your consent choice: {ex.Message}",
                "TradeCopilot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        Accepted = true;
        DialogResult = true;
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        DialogResult = false;
    }
}
