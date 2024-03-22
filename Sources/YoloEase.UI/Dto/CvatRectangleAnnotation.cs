namespace YoloEase.UI.Dto;

public sealed record CvatRectangleAnnotation
{
    public int LabelId { get; init; }
    public int FrameIndex { get; init; }
    public RectangleD BoundingBox { get; init; } 
}