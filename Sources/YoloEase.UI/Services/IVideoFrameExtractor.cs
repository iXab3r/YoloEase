using System.Threading;
using PoeShared.Scaffolding;

namespace YoloEase.UI.Services;

/// <summary>
/// Extracts annotation-ready still frames from a source video file.
/// </summary>
public interface IVideoFrameExtractor
{
    /// <summary>
    /// Reads video metadata needed by the frame extraction UI.
    /// </summary>
    Task<VideoFrameProbe> ProbeAsync(FileInfo inputFile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts the selected video frames to image files in the requested output directory.
    /// </summary>
    Task<VideoFrameExtractionResult> ExtractAsync(
        VideoFrameExtractionRequest request,
        IProgressReporter? progressReporter = default,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Describes a video stream as seen by the frame extraction service.
/// </summary>
public sealed record VideoFrameProbe(
    TimeSpan Duration,
    double FramesPerSecond,
    long FrameCount,
    int Width,
    int Height);

/// <summary>
/// Defines the video frame range and output directory for one extraction run.
/// </summary>
public sealed record VideoFrameExtractionRequest(
    FileInfo InputFile,
    DirectoryInfo OutputDirectory,
    long StartFrameIndex,
    long EndFrameIndex,
    int FrameNth);

/// <summary>
/// Summarizes the files produced by one extraction run.
/// </summary>
public sealed record VideoFrameExtractionResult(
    DirectoryInfo OutputDirectory,
    int SelectedFrameCount,
    int SavedFrameCount,
    int SkippedExistingFrameCount);

/// <summary>
/// Reports video probing or extraction failures in user-facing workflows.
/// </summary>
public sealed class VideoFrameExtractionException : Exception
{
    public VideoFrameExtractionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
