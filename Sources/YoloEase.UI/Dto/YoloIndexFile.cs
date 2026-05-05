namespace YoloEase.UI.Dto;

/// <summary>
/// Represents a YOLO data.yaml-style index file for train, validation, and test splits.
/// </summary>
public sealed record YoloIndexFile
{
    public string Train { get; set; }
    public string Val { get; set; }
    public string Test { get; set; }
    public int Nc { get; set; }
    public List<string> Names { get; set; }
}
