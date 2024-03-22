namespace YoloEase.UI.Yolo;

public sealed record Yolo8PredictProgressUpdate
{
    public int ImageCurrent { get; init; }
    public int ImageMax { get; init; }
    public float ProgressPercentage { get; init; }
}