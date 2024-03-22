namespace YoloEase.UI.Yolo;

public sealed record Yolo8TrainArguments
{
    public string Model { get; init; }
    public string DataYamlPath { get; init; }
    public string ImageSize { get; init; }
    public int? Epochs { get; init; }
    public string AdditionalArguments { get; init; }
    public DirectoryInfo OutputDirectory { get; init; }
}