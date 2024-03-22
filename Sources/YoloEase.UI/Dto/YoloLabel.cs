namespace YoloEase.UI.Dto;

public sealed record YoloLabel
{
    public int Id { get; init; }
    public RectangleD BoundingBox { get; init; } 
    public float? Confidence { get; init; }
}