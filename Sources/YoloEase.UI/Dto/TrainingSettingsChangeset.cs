namespace YoloEase.UI.Dto;

/// <summary>
/// Describes training setting changes displayed in the timeline.
/// </summary>
public sealed record TrainingSettingsChangeset : Changeset
{
    public static readonly TrainingSettingsChangeset Empty = new();
    
    public ModelTrainingSettings Previous { get; init; }
    
    public ModelTrainingSettings Current { get; init; }

    public override bool IsEmpty => Previous == null && Current == null;
}
