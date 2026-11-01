namespace OD.Converter.Models;

/// <summary>Identifies a shared audio/video workflow.</summary>
public enum ProcessingAction
{
    /// <summary>Exports the selected soundtrack.</summary>
    Extract,
    /// <summary>Separates vocals and background.</summary>
    Separate,
    /// <summary>Transcribes the selected soundtrack.</summary>
    Transcribe,
    /// <summary>Separates vocals before transcription.</summary>
    SeparateTranscribe
}

/// <summary>Identifies one finalized export.</summary>
public enum ExportKind
{
    /// <summary>The complete selected soundtrack.</summary>
    Original,
    /// <summary>Estimated vocals.</summary>
    Vocals,
    /// <summary>Estimated background.</summary>
    Background,
    /// <summary>UTF-8 global transcript.</summary>
    Transcript,
    /// <summary>Global subtitles on the presentation timeline.</summary>
    Subtitles
}

/// <summary>Describes a workflow independently of the source container and user interface.</summary>
public sealed record ProcessingRequest(
    string InputPath,
    ProcessingAction Action,
    int? AudioStreamIndex,
    string OutputDirectory,
    string OutputPrefix,
    IReadOnlyList<ExportKind> Exports,
    string Language,
    ConversionOptions AudioOptions);

/// <summary>Combines the request with an explicitly resolved track and its presentation reference.</summary>
public sealed record ProcessingPlan(ProcessingRequest Request, MediaSource Source, AudioTrackInfo Track);

/// <summary>Describes a completed file; partial files must never be added to this collection.</summary>
public sealed record ProcessingOutput(ExportKind Kind, string Path);

/// <summary>Reports global success, failure or cancellation while retaining already finalized outputs.</summary>
public sealed record ProcessingResult(
    int ExitCode,
    IReadOnlyList<ProcessingOutput> Outputs,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage);
