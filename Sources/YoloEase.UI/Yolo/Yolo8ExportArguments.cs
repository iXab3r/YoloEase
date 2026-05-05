namespace YoloEase.UI.Yolo;

/// <summary>
/// Captures the source model and target format for a YOLO export operation.
/// </summary>
public sealed record Yolo8ExportArguments
{
    public string Model { get; init; }
    public string Format { get; init; }
    public int? Opset { get; init; }
}
