namespace YoloEase.UI.Yolo;

public sealed record Yolo8PredictArguments
{
    public required string Source { get; init; }
    public required DirectoryInfo WorkingDirectory { get; init; }
    public string Model { get; init; }
    public string ImageSize { get; init; }
    public float? Confidence { get; init; }
    public float? IoU { get; init; }
    public string AdditionalArguments { get; init; }
    public bool SaveTxt { get; init; }
}