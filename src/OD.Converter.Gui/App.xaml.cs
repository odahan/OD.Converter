using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using OD.Converter.Services;
using OD.Converter.Services.Modules;

namespace OD.Converter.Gui;

/// <summary>Composes the application and applies system high-contrast changes to its resources.</summary>
public partial class App : Application
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(30) };

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SystemParameters.StaticPropertyChanged += SystemParametersChanged;
        ApplyContrast();

        MainViewModel? viewModel = null;
        var progress = new DispatcherProgress(Dispatcher, value => viewModel?.Report(value));
        var runner = new ModuleProcessRunner(output =>
            progress.Report(new MediaProgress(MediaProgressKind.NativeOutput, output)));
        var recipes = BuiltInModuleRecipes.Create(runner);
        var modules = new ModuleManager(new ModulePaths(), new ModuleDownloader(http), recipes, progress);
        var files = new Services.FileSystem();
        var locator = new FfmpegLocator();
        var validator = new ConversionOptionsValidator(files);
        var ffmpeg = new FfmpegConverter(validator);
        var conversion = new ConversionService(ffmpeg, locator, files, ffmpeg, validator);
        var processing = new ProcessingService(locator, files, new FfprobeService(), modules,
            new AudioExtractionService(ffmpeg, ffmpeg, progress),
            new AudioSeparationService(modules, runner, ffmpeg, ffmpeg, progress),
            new AudioTranscriptionService(modules, runner, ffmpeg, ffmpeg, progress));

        if (e.Args is ["--validate-bundle", var report])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await BundleValidation.RunAsync(report, conversion, processing, locator, recipes, async () =>
            {
                var smokeWindow = new MainWindow();
                var smokeModules = new ModuleManager(new ModulePaths(Path.Combine(Path.GetDirectoryName(report)!, "smoke-module-state")),
                    new ModuleDownloader(http), recipes);
                var smokeViewModel = new MainViewModel(new Desktop(() => smokeWindow), conversion, processing, smokeModules,
                    new UserGuideService(), null);
                smokeWindow.DataContext = smokeViewModel;
                smokeWindow.ShowActivated = false;
                smokeWindow.ShowInTaskbar = false;
                smokeWindow.Opacity = 0;
                smokeWindow.Show();
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                await ValidateComboBoxAsync(smokeWindow.ConversionMode);
                if (smokeViewModel.ConversionMode != "Multiple files")
                    throw new InvalidOperationException("The selected input mode did not reach the view model.");
                smokeViewModel.SelectedPage = 3;
                await (smokeViewModel.RunCommand.ExecutionTask ?? throw new InvalidOperationException("Module refresh did not start."));
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                if (smokeViewModel.State != "Completed" || smokeViewModel.ModuleStateText != "Not installed" ||
                    smokeWindow.WorkflowTabs.SelectedIndex != 3 || !smokeWindow.WorkflowTabs.IsEnabled)
                    throw new InvalidOperationException($"Optional modules did not finish its offline refresh. " +
                        $"State: {smokeViewModel.State}. Module: {smokeViewModel.ModuleStateText}. " +
                        $"Tab: {smokeWindow.WorkflowTabs.SelectedIndex}. Enabled: {smokeWindow.WorkflowTabs.IsEnabled}. " +
                        string.Join(" ", smokeViewModel.Notices));
                smokeWindow.Close();
            }));
            return;
        }

        var window = new MainWindow();
        viewModel = new MainViewModel(new Desktop(() => window), conversion, processing, modules,
            new UserGuideService(), e.Args.Length == 1 ? e.Args[0] : null);
        window.DataContext = viewModel;
        MainWindow = window;
        window.Show();
    }

    private static async Task ValidateComboBoxAsync(ComboBox combo)
    {
        var template = combo.Template;
        var toggle = (ToggleButton)template.FindName("DropDownToggle", combo);
        var popup = (Popup)template.FindName("PART_Popup", combo);
        popup.Child.Opacity = 0;

        var hit = combo.InputHitTest(new Point(combo.ActualWidth / 2, combo.ActualHeight / 2)) as Visual;
        while (hit is not null && hit != toggle) hit = VisualTreeHelper.GetParent(hit) as Visual;
        if (hit is null) throw new InvalidOperationException("The visible combo box does not route clicks to its toggle.");

        ((IToggleProvider)new ToggleButtonAutomationPeer(toggle)).Toggle();
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        if (!combo.IsDropDownOpen || !popup.IsOpen)
            throw new InvalidOperationException("The combo box toggle did not open its list.");

        combo.SetCurrentValue(Selector.SelectedIndexProperty, 1);
        combo.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateSource();
        combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= SystemParametersChanged;
        http.Dispose();
        base.OnExit(e);
    }

    private void SystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) ApplyContrast();
    }

    private sealed class DispatcherProgress(Dispatcher dispatcher, Action<MediaProgress> handler) : IProgress<MediaProgress>
    {
        public void Report(MediaProgress value) => _ = dispatcher.InvokeAsync(() => handler(value));
    }

    private void ApplyContrast()
    {
        var contrast = SystemParameters.HighContrast;
#pragma warning disable WPF0001
        ThemeMode = contrast ? ThemeMode.None : ThemeMode.Dark;
#pragma warning restore WPF0001
        UseSystemBrush("TextBrush", SystemColors.WindowTextBrush);
        UseSystemBrush("ControlBrush", SystemColors.ControlBrush);
        UseSystemBrush("InputBrush", SystemColors.WindowBrush);
        UseSystemBrush("HoverBrush", SystemColors.HighlightBrush);
        UseSystemBrush("PressedBrush", SystemColors.HighlightBrush);
        UseSystemBrush("SelectedBrush", SystemColors.HighlightBrush);
        UseSystemBrush("SelectedTextBrush", SystemColors.HighlightTextBrush);
        UseSystemBrush("DisabledTextBrush", SystemColors.GrayTextBrush);
        UseSystemBrush("PageBrush", SystemColors.WindowBrush);
        UseSystemBrush("PanelBrush", SystemColors.WindowBrush);
        UseSystemBrush("LineBrush", SystemColors.WindowTextBrush);
        UseSystemBrush("SecondaryTextBrush", SystemColors.WindowTextBrush);
        UseSystemBrush("FocusBrush", SystemColors.HighlightBrush);
        UseSystemBrush("SuccessBrush", SystemColors.WindowTextBrush);
        UseSystemBrush("WarningBrush", SystemColors.WindowTextBrush);
        UseSystemBrush("ErrorBrush", SystemColors.WindowTextBrush);

        void UseSystemBrush(string key, Brush brush)
        {
            if (contrast) Resources[key] = brush;
            else Resources.Remove(key);
        }
    }
}
