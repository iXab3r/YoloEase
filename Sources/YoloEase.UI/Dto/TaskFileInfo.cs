namespace YoloEase.UI.Dto;

public sealed record TaskFileInfo
{
    public string FileName { get; init; }
    
    public int? TaskId { get; init; }
}