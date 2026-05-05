namespace YoloEase.UI.Dto;

/// <summary>
/// Describes a trained model file produced by the YOLO training pipeline.
/// </summary>
public sealed record TrainedModelFileInfo
{
    public FileInfo ModelFile { get; init; }
}
