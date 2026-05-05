namespace YoloEase.UI.Dto;

/// <summary>
/// Describes prediction output generated for a dataset revision.
/// </summary>
public sealed record DatasetPredictInfo
{
    public required DirectoryInfo OutputDirectory { get; init; }
    
    public required TrainedModelFileInfo ModelFile { get; init; }

    public required PredictInfo[] Predictions { get; init; } = Array.Empty<PredictInfo>();
}
