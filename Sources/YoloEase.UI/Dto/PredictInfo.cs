using YoloEase.UI.Yolo;

namespace YoloEase.UI.Dto;

public sealed record PredictInfo
{
    public FileInfo File { get; init; }

    public YoloPrediction[] Labels { get; init; } = Array.Empty<YoloPrediction>();
}