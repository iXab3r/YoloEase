namespace YoloEase.UI.Yolo;

/// <summary>
/// Captures the command-line settings for a YOLO training run.
/// </summary>
public sealed record Yolo8TrainArguments
{
    public string Model { get; init; }
    public string DataYamlPath { get; init; }
    public string ImageSize { get; init; }
    public int? Epochs { get; init; }
    public string AdditionalArguments { get; init; }
    public int MaxCpuCoresCount { get; init; }
    public DirectoryInfo OutputDirectory { get; init; }
}
