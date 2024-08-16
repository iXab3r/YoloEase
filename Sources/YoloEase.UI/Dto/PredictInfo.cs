using YoloEase.UI.Yolo;

namespace YoloEase.UI.Dto;

public sealed record PredictInfo
{
    public FileInfo File { get; init; }

    public YoloPrediction[] Labels { get; init; } = Array.Empty<YoloPrediction>();
}

public sealed record FileLabel(FileInfo File, YoloPrediction Label)
{
    public FileInfo File { get; init; } = File;

    public YoloPrediction Label { get; init; } = Label;
}