namespace YoloEase.UI.Yolo;

/// <summary>
/// Reports parsed prediction progress from the YOLO command-line output.
/// </summary>
public sealed record Yolo8PredictProgressUpdate
{
    public int ImageCurrent { get; init; }
    public int ImageMax { get; init; }
    public float ProgressPercentage { get; init; }
}
