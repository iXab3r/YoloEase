using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TaskAnnotation;

public sealed record AutoAnnotationModelResolution(FileInfo ModelFile, string Sha256);

public sealed record AutoAnnotationRunRequest
{
    public required AutoAnnotationModelConfig Model { get; init; }

    public required IReadOnlyList<AutoAnnotationFrameInput> Frames { get; init; }

    public required IReadOnlyList<AnnotationLabelInfo> ProjectLabels { get; init; }

    public required IReadOnlyList<TrainedModelFileInfo> TrainedModels { get; init; }
}

public sealed record AutoAnnotationFrameInput(int FrameIndex, FileInfo ImageFile, int Width, int Height);

public sealed record AutoAnnotationRunProgress(
    AutoAnnotationModelConfig Model,
    int CompletedFrames,
    int TotalFrames,
    int CurrentFrameIndex,
    string Text);

public sealed class AutoAnnotationRunResult
{
    public AutoAnnotationRunResult(string modelEntryId, FileInfo resolvedModelFile, string resolvedModelHash)
    {
        ModelEntryId = modelEntryId;
        ResolvedModelFile = resolvedModelFile;
        ResolvedModelHash = resolvedModelHash;
    }

    public string ModelEntryId { get; }

    public FileInfo ResolvedModelFile { get; }

    public string ResolvedModelHash { get; }

    public List<AutoAnnotationFrameResult> FrameResults { get; } = new();

    public int AddedCount { get; set; }

    public int ReplacedCount { get; set; }

    public int SkippedCount { get; set; }

    public int ErrorCount { get; set; }

    public DateTimeOffset Timestamp { get; } = DateTimeOffset.Now;
}

public sealed record AutoAnnotationFrameResult(
    int FrameIndex,
    IReadOnlyList<AutoAnnotationPrediction> Predictions,
    int SkippedCount);

public sealed record AutoAnnotationPrediction
{
    public int FrameIndex { get; init; }

    public int ProjectLabelId { get; init; }

    public RectangleD BoundingBox { get; init; }

    public string Source { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public int ModelLabelIndex { get; init; }

    public string ModelLabelName { get; init; } = string.Empty;
}

internal sealed record AutoAnnotationResolvedLabelMapping(
    AutoAnnotationLabelMapping Mapping,
    AnnotationLabelInfo ProjectLabel);
