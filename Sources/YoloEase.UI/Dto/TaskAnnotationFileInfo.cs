namespace YoloEase.UI.Dto;

/// <summary>
/// Describes an annotation XML file associated with a task.
/// </summary>
public sealed record TaskAnnotationFileInfo
{
    public required int TaskId { get; init; }
    
    public required string TaskName { get; init; }

    public string? FilePath { get; init; }

    public long FileSize { get; init; }
}
