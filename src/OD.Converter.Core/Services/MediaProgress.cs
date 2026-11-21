namespace OD.Converter.Services;

/// <summary>Identifies a processing stage or a diagnostic emitted by a media engine.</summary>
public enum MediaProgressKind
{
    /// <summary>Preparing PCM on the source timeline.</summary>
    PreparingAudio,
    /// <summary>Separating stems with the private engine.</summary>
    Separating,
    /// <summary>Transcribing with the private engine.</summary>
    Transcribing,
    /// <summary>Encoding a requested audio export.</summary>
    Encoding,
    /// <summary>Technical details of an operation.</summary>
    Diagnostic,
    /// <summary>A recoverable outcome that needs attention.</summary>
    Warning,
    /// <summary>A warning about temporary file cleanup.</summary>
    CleanupWarning,
    /// <summary>Output captured from a private native process.</summary>
    NativeOutput
}

/// <summary>Reports a stage, diagnostic, warning or engine output independently of the presentation.</summary>
public sealed record MediaProgress(MediaProgressKind Kind, string Message);
