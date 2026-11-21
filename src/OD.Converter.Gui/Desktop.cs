using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace OD.Converter.Gui;

/// <summary>Provides the ViewModels with native pickers and application-owned dialogs.</summary>
internal sealed class Desktop(Func<Window> owner)
{
    public string[] PickFiles(bool multiple)
    {
        var dialog = new OpenFileDialog
        {
            Title = multiple ? "Select source files" : "Select a source file",
            Multiselect = multiple,
            Filter = "All files (*.*)|*.*"
        };
        return dialog.ShowDialog(owner()) == true ? dialog.FileNames : [];
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(owner()) == true ? dialog.FolderName : null;
    }

    public string? PickOutput(string filename, string extension)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Choose a destination file",
            FileName = filename,
            DefaultExt = extension,
            OverwritePrompt = false
        };
        return dialog.ShowDialog(owner()) == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string text, string accept) =>
        new DetailDialog(owner(), title, text, accept).ShowDialog() == true;

    public void Show(string title, string text) => new DetailDialog(owner(), title, text).ShowDialog();

    public static void Copy(string text) => Clipboard.SetText(text);

    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public static void ShowFile(string path)
    {
        var request = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        request.ArgumentList.Add("/select,");
        request.ArgumentList.Add(path);
        Process.Start(request);
    }
}
