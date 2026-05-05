using YoloEase.UI.Yolo;

namespace YoloEase.UI.Dto;

/// <summary>
/// Describes prediction settings and selected model state for a project.
/// </summary>
public sealed record PredictInfo
{
    public FileInfo File { get; init; }

    public YoloPrediction[] Labels { get; init; } = Array.Empty<YoloPrediction>();
}

/// <summary>
/// Pairs an image file with one predicted label.
/// </summary>
public sealed record FileLabel(FileInfo File, YoloPrediction Label)
{
    public FileInfo File { get; init; } = File;

    public YoloPrediction Label { get; init; } = Label;
}
