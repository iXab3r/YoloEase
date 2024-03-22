namespace YoloEase.UI.Yolo;

public sealed record Yolo8TrainProgressUpdate
{
    public int EpochCurrent { get; init; }
    public int EpochMax  { get; init; }
    public float EpochPercentage { get; init; }
    public float ProgressPercentage { get; init; }
    public string VideoRAM  { get; init; }
}