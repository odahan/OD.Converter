using System.ComponentModel;

namespace OD.Converter.Gui;

/// <summary>Waits for the active command to finish cleaning up before closing the window.</summary>
public partial class MainWindow : DarkWindow
{
    private bool stopping;

    public MainWindow() => InitializeComponent();

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is not MainViewModel { IsBusy: true } viewModel) return;
        e.Cancel = true;
        if (stopping) return;

        var dialog = new DetailDialog(this, "Stop and close?",
            "An operation is running. The window will close after it stops and removes its temporary files.", "_Stop and close");
        if (dialog.ShowDialog() != true) return;

        stopping = true;
        await viewModel.CancelAndWaitAsync();
        _ = Dispatcher.BeginInvoke(Close);
    }
}
