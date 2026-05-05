namespace YoloEase.UI.Dto;

/// <summary>
/// Describes a local file that belongs to an annotation task.
/// </summary>
public sealed record TaskFileInfo
{
    public string FileName { get; init; }
    
    public int? TaskId { get; init; }
}
