using YoloEase.UI.Dto;

namespace YoloEase.UI.TaskAnnotation;

public sealed class AutoAnnotationSuggestion
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public int FrameIndex { get; init; }

    public int LabelId { get; init; }

    public RectangleD BoundingBox { get; init; }

    public string ModelEntryId { get; init; } = string.Empty;

    public string ModelDisplayName { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public int ModelLabelIndex { get; init; }

    public string ModelLabelName { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public TaskAnnotationWindow.EditorShape ToManualShape()
    {
        return new TaskAnnotationWindow.EditorShape
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = CvatAnnotationShapeKind.Rectangle,
            FrameIndex = FrameIndex,
            LabelId = LabelId,
            X = BoundingBox.X,
            Y = BoundingBox.Y,
            Width = BoundingBox.Width,
            Height = BoundingBox.Height,
            RotationDegrees = 0,
            Source = "manual",
            Confidence = null,
        };
    }

    public static AutoAnnotationSuggestion FromPrediction(
        AutoAnnotationModelConfig model,
        AutoAnnotationPrediction prediction)
    {
        return new AutoAnnotationSuggestion
        {
            Id = Guid.NewGuid().ToString("N"),
            FrameIndex = prediction.FrameIndex,
            LabelId = prediction.ProjectLabelId,
            BoundingBox = prediction.BoundingBox,
            ModelEntryId = model.Id,
            ModelDisplayName = model.DisplayName,
            Confidence = prediction.Confidence,
            ModelLabelIndex = prediction.ModelLabelIndex,
            ModelLabelName = prediction.ModelLabelName,
        };
    }
}

internal static class AutoAnnotationSuggestionOperations
{
    public static int ReplaceFrameSuggestions(
        IList<AutoAnnotationSuggestion> suggestions,
        AutoAnnotationModelConfig model,
        AutoAnnotationFrameResult frameResult)
    {
        var replaced = 0;
        for (var i = suggestions.Count - 1; i >= 0; i--)
        {
            var suggestion = suggestions[i];
            if (suggestion.FrameIndex != frameResult.FrameIndex ||
                !string.Equals(suggestion.ModelEntryId, model.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            suggestions.RemoveAt(i);
            replaced++;
        }

        foreach (var prediction in frameResult.Predictions)
        {
            suggestions.Add(AutoAnnotationSuggestion.FromPrediction(model, prediction));
        }

        return replaced;
    }
}
