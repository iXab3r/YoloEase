namespace YoloEase.UI.Dto;

/// <summary>
/// Base type for project-level changes displayed in the training timeline.
/// </summary>
public abstract record Changeset
{
    public abstract bool IsEmpty { get; }
}
