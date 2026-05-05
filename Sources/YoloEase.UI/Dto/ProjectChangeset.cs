namespace YoloEase.UI.Dto;

/// <summary>
/// Describes a project identity change shown in the timeline.
/// </summary>
public sealed record ProjectChangeset : Changeset
{
    public static readonly ProjectChangeset Empty = new();

    public IReadOnlyList<int> ChangedAnnotatedTasks { get; init; } = new List<int>();
    public override bool IsEmpty => ChangedAnnotatedTasks.Count <= 0;
}
