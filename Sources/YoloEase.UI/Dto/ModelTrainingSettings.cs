namespace YoloEase.UI.Dto;

/// <summary>
/// Captures user-facing settings that influence model training.
/// </summary>
public sealed record ModelTrainingSettings
{
    public string Model { get; init; }
    
    public string ModelSize { get; init; }
    
    public int Epochs { get; init; }
}
