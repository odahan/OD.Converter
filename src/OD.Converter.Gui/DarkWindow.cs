using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OD.Converter.Gui;

/// <summary>Keeps native system chrome legible with the forced dark Fluent theme.</summary>
public class DarkWindow : Window
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        SystemParameters.StaticPropertyChanged += SystemParametersChanged;
        UpdateChrome();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        UpdateChrome();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        UpdateChrome();
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= SystemParametersChanged;
        base.OnClosed(e);
    }

    private void SystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) UpdateChrome();
    }

    private void UpdateChrome()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var contrast = SystemParameters.HighContrast;
        SetAttribute(handle, DwmAttribute.ImmersiveDarkMode, contrast ? 0u : 1u);
        SetAttribute(handle, DwmAttribute.CaptionColor, contrast ? DefaultColor : IsActive ? 0x00201C19u : 0x00272320u);
        SetAttribute(handle, DwmAttribute.TextColor, contrast ? DefaultColor : IsActive ? 0x00F5F3F1u : 0x00D4CBC2u);
        SetAttribute(handle, DwmAttribute.BorderColor, contrast ? DefaultColor : IsActive ? 0x0066584Bu : 0x00524941u);
    }

    private const uint DefaultColor = 0xFFFFFFFF;

    private enum DwmAttribute
    {
        ImmersiveDarkMode = 20,
        BorderColor = 34,
        CaptionColor = 35,
        TextColor = 36
    }

    private static void SetAttribute(IntPtr window, DwmAttribute attribute, uint value) =>
        _ = DwmSetWindowAttribute(window, attribute, in value, sizeof(uint));

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, DwmAttribute attribute, in uint value, int size);
}
