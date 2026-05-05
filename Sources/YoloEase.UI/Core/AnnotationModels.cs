namespace YoloEase.UI.Core;

/// <summary>
/// Selects whether annotation data is read from CVAT or from local offline storage.
/// </summary>
public enum AnnotationBackendMode
{
    Cvat = 0,
    Offline = 1,
}

/// <summary>
/// Represents the local task lifecycle states shared between CVAT and offline backends.
/// </summary>
public enum AnnotationTaskStatus
{
    New = 0,
    InProgress = 1,
    Completed = 2,
}

/// <summary>
/// Describes one annotation project shown in project selectors.
/// </summary>
public sealed record AnnotationProjectInfoItem
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Describes an annotation task and the revision used for cache invalidation.
/// </summary>
public sealed record AnnotationTaskInfo
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public AnnotationTaskStatus Status { get; init; }

    public int Revision { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>
/// Describes an annotation job associated with a task.
/// </summary>
public sealed record AnnotationJobInfo
{
    public int Id { get; init; }

    public int TaskId { get; init; }

    public string Name { get; init; } = string.Empty;

    public AnnotationTaskStatus Status { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>
/// Describes an annotation label, including its stable id and display color.
/// </summary>
public sealed record AnnotationLabelInfo
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Color { get; init; } = "#4f7cff";
}

/// <summary>
/// Describes the authenticated annotation user.
/// </summary>
public sealed record AnnotationUserInfo
{
    public string Username { get; init; } = string.Empty;
}

/// <summary>
/// Describes one task frame with its image dimensions.
/// </summary>
public sealed record AnnotationFrameInfo
{
    public int Index { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Width { get; init; }

    public int Height { get; init; }
}

/// <summary>
/// Holds task frame metadata loaded for the annotation editor.
/// </summary>
public sealed record AnnotationDataMeta
{
    public IReadOnlyList<AnnotationFrameInfo> Frames { get; init; } = Array.Empty<AnnotationFrameInfo>();
}

/// <summary>
/// Reports the outcome of saving annotation XML back to the active backend.
/// </summary>
public sealed record AnnotationUpdateResult
{
    public int ShapesCount { get; init; }
}

/// <summary>
/// Stores the latest known revision for an annotation task.
/// </summary>
public sealed record TaskRevisionInfo
{
    public int TaskId { get; init; }

    public int Revision { get; init; }
}
