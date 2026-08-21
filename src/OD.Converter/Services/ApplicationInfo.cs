using System.Reflection;

namespace OD.Converter.Services;

/// <summary>Provides application metadata for display.</summary>
public static class ApplicationInfo
{
    /// <summary>Gets the current application version.</summary>
    public static string Version => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";
}
