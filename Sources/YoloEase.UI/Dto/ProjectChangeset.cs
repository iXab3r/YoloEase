namespace YoloEase.UI.Dto;

public sealed record ProjectChangeset : Changeset
{
    public static readonly ProjectChangeset Empty = new();

    public IReadOnlyList<int> NewAnnotatedTasks { get; init; } = new List<int>();
    public override bool IsEmpty => NewAnnotatedTasks.Count <= 0;
}