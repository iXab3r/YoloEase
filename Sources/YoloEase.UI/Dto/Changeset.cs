namespace YoloEase.UI.Dto;

public abstract record Changeset
{
    public abstract bool IsEmpty { get; }
}