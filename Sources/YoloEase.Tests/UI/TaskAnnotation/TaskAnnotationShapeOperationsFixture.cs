using Shouldly;
using YoloEase.UI.TaskAnnotation;

namespace YoloEase.Tests.UI.TaskAnnotation;

public class TaskAnnotationShapeOperationsFixture
{
    [Test]
    public void ShouldCopyShapesToTargetFrameAsManualAnnotations()
    {
        // Given
        var source = new TaskAnnotationWindow.EditorShape
        {
            Id = "source-shape",
            FrameIndex = 2,
            LabelId = 7,
            X = 10,
            Y = 20,
            Width = 30,
            Height = 40,
            RotationDegrees = 15,
            Source = "automatic:model-a",
            Confidence = 0.86,
        };

        // When
        var copied = TaskAnnotationShapeOperations.CopyToFrameAsManual(new[] { source }, 3, 100, 100);

        // Then
        copied.Count.ShouldBe(1);
        var shape = copied.Single();
        shape.Id.ShouldNotBe(source.Id);
        shape.FrameIndex.ShouldBe(3);
        shape.LabelId.ShouldBe(source.LabelId);
        shape.X.ShouldBe(source.X);
        shape.Y.ShouldBe(source.Y);
        shape.Width.ShouldBe(source.Width);
        shape.Height.ShouldBe(source.Height);
        shape.RotationDegrees.ShouldBe(source.RotationDegrees);
        shape.Source.ShouldBe("manual");
        shape.Confidence.ShouldBeNull();
        source.Source.ShouldBe("automatic:model-a");
        source.Confidence.ShouldBe(0.86);
    }

    [Test]
    public void ShouldClampCopiedShapesToTargetFrameBounds()
    {
        // Given
        var source = new TaskAnnotationWindow.EditorShape
        {
            Id = "source-shape",
            FrameIndex = 0,
            LabelId = 1,
            X = 90,
            Y = 90,
            Width = 20,
            Height = 30,
            Source = "manual",
        };

        // When
        var copied = TaskAnnotationShapeOperations.CopyToFrameAsManual(new[] { source }, 1, 100, 100);

        // Then
        var shape = copied.Single();
        shape.X.ShouldBe(80);
        shape.Y.ShouldBe(70);
        shape.Width.ShouldBe(20);
        shape.Height.ShouldBe(30);
    }
}
