namespace YoloEase.UI.Dto;

/// <summary>
/// Describes a local data source folder used to create or update annotation tasks.
/// </summary>
public record DatasourceInfo
{
    public DirectoryInfo Directory { get; init; }
}
