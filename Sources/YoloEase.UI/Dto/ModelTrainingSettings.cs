namespace YoloEase.UI.Dto;

public sealed record ModelTrainingSettings
{
    public string Model { get; init; }
    
    public string ModelSize { get; init; }
    
    public int Epochs { get; init; }
}