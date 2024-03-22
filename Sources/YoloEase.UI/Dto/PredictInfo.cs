namespace YoloEase.UI.Dto;

public sealed record PredictInfo
{
    public FileInfo File { get; init; }

    public YoloLabel[] Labels { get; init; } = Array.Empty<YoloLabel>();
}