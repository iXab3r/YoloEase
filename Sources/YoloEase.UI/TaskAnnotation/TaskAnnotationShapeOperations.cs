using System.Linq;

namespace YoloEase.UI.TaskAnnotation;

internal static class TaskAnnotationShapeOperations
{
    private const string ManualSource = "manual";

    public static IReadOnlyList<TaskAnnotationWindow.EditorShape> CopyToFrameAsManual(
        IEnumerable<TaskAnnotationWindow.EditorShape> sourceShapes,
        int targetFrameIndex,
        double targetImageWidth,
        double targetImageHeight)
    {
        return sourceShapes
            .Select(shape => CopyToFrameAsManual(shape, targetFrameIndex, targetImageWidth, targetImageHeight))
            .ToArray();
    }

    private static TaskAnnotationWindow.EditorShape CopyToFrameAsManual(
        TaskAnnotationWindow.EditorShape sourceShape,
        int targetFrameIndex,
        double targetImageWidth,
        double targetImageHeight)
    {
        var clone = sourceShape.CloneWithId(Guid.NewGuid().ToString("N"));
        clone.FrameIndex = targetFrameIndex;
        clone.Source = ManualSource;
        clone.Confidence = null;

        if (targetImageWidth > 0 && targetImageHeight > 0)
        {
            clone.ClampUnrotatedBounds(targetImageWidth, targetImageHeight);
        }

        return clone;
    }
}
