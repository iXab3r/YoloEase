namespace YoloEase.UI.Dto;

/// <summary>
/// Represents a rectangle annotation imported from CVAT or offline annotation XML.
/// </summary>
public sealed record CvatRectangleAnnotation
{
    public CvatAnnotationShapeKind Kind { get; init; } = CvatAnnotationShapeKind.Rectangle;
    public int LabelId { get; init; }
    public int FrameIndex { get; init; }
    public RectangleD BoundingBox { get; init; }
    public double RotationDegrees { get; init; }
    public string? Source { get; init; }
}
