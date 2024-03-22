namespace YoloEase.UI.Dto;

public sealed record TaskAnnotationFileInfo
{
    public string FilePath { get; init; }
    
    public int TaskId { get; init; }
    
    public string TaskName { get; init; }
    
    public long FileSize { get; init; }
}