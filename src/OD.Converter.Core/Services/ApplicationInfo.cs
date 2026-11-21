namespace OD.Converter.Services;

/// <summary>Provides application metadata for display.</summary>
public static class ApplicationInfo
{
    /// <summary>Gets the author's voluntary support page, shared with HarmoTools.</summary>
    public const string SupportUrl = "https://buymeacoffee.com/knhsynths";

    /// <summary>Gets the current application version.</summary>
    public static string Version => typeof(ApplicationInfo).Assembly.GetName().Version?.ToString(3) ?? "unknown";
}
