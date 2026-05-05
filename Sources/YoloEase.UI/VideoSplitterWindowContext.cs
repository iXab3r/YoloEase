using YoloEase.UI.Core;

namespace YoloEase.UI;

/// <summary>
/// Supplies the project and selected source video to the standalone video frame extraction window.
/// </summary>
public sealed class VideoSplitterWindowContext : RefreshableReactiveObject
{
    /// <summary>
    /// Creates window context for one extraction run.
    /// </summary>
    public VideoSplitterWindowContext(YoloEaseProject project, FileInfo videoFile)
    {
        Project = project;
        VideoFile = videoFile;
    }

    /// <summary>
    /// Project that will receive the extracted frame directory as a local data source.
    /// </summary>
    public YoloEaseProject Project { get; }

    /// <summary>
    /// Source video selected by the user for frame extraction.
    /// </summary>
    public FileInfo VideoFile { get; }

    protected override Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
        return Task.CompletedTask;
    }
}
