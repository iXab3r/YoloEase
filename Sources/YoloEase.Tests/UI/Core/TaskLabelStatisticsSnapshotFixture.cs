using Shouldly;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.Tests.UI.Core;

public class TaskLabelStatisticsSnapshotFixture
{
    /// <summary>
    /// WHAT: Verifies label statistics count both shapes and affected task files.
    /// HOW: Builds a two-task snapshot with repeated labels on one frame and checks project and task totals.
    /// </summary>
    [Test]
    public void ShouldCountLabelsByAnnotationsAndFiles()
    {
        // Given
        var labels = new[]
        {
            new AnnotationLabelInfo { Id = 1, Name = "Flower", Color = "#ddff33" },
            new AnnotationLabelInfo { Id = 2, Name = "Bomb", Color = "#fa3253" },
        };
        var tasks = new[]
        {
            new AnnotationTaskInfo { Id = 10, Name = "Task 10" },
            new AnnotationTaskInfo { Id = 11, Name = "Task 11" },
        };
        var files = new[]
        {
            new TaskFileInfo { TaskId = 10, FileName = "a.png" },
            new TaskFileInfo { TaskId = 10, FileName = "b.png" },
            new TaskFileInfo { TaskId = 10, FileName = "c.png" },
            new TaskFileInfo { TaskId = 11, FileName = "d.png" },
        };
        var annotationsByTask = new Dictionary<int, IReadOnlyList<CvatRectangleAnnotation>>
        {
            [10] = new[]
            {
                new CvatRectangleAnnotation { LabelId = 1, FrameIndex = 0 },
                new CvatRectangleAnnotation { LabelId = 1, FrameIndex = 0 },
                new CvatRectangleAnnotation { LabelId = 2, FrameIndex = 1 },
            },
            [11] = new[]
            {
                new CvatRectangleAnnotation { LabelId = 1, FrameIndex = 0 },
            },
        };

        // When
        var snapshot = TaskLabelStatisticsSnapshot.Create(tasks, files, labels, annotationsByTask);

        // Then
        snapshot.TotalFiles.ShouldBe(4);
        snapshot.TotalAnnotations.ShouldBe(4);
        snapshot.Labels.Count.ShouldBe(2);

        var flower = snapshot.Labels.Single(x => x.LabelName == "Flower");
        flower.AnnotationCount.ShouldBe(3);
        flower.FileCount.ShouldBe(2);
        flower.FilePercent.ShouldBe(0.5);

        var bomb = snapshot.Labels.Single(x => x.LabelName == "Bomb");
        bomb.AnnotationCount.ShouldBe(1);
        bomb.FileCount.ShouldBe(1);
        bomb.FilePercent.ShouldBe(0.25);

        var task = snapshot.Tasks.Single(x => x.TaskId == 10);
        task.FileCount.ShouldBe(3);
        task.AnnotatedFileCount.ShouldBe(2);
        task.AnnotationCount.ShouldBe(3);
        task.Labels.Single(x => x.LabelName == "Flower").FilePercent.ShouldBe(1d / 3d);
    }

    /// <summary>
    /// WHAT: Verifies annotation read failures remain visible in the task snapshot.
    /// HOW: Creates a task with an error and no annotations, then checks the error and failed count.
    /// </summary>
    [Test]
    public void ShouldKeepTaskAnnotationErrorsInSnapshot()
    {
        // Given
        var tasks = new[]
        {
            new AnnotationTaskInfo { Id = 10, Name = "Task 10" },
        };
        var files = new[]
        {
            new TaskFileInfo { TaskId = 10, FileName = "a.png" },
        };
        var errors = new Dictionary<int, string>
        {
            [10] = "Bad annotation XML",
        };

        // When
        var snapshot = TaskLabelStatisticsSnapshot.Create(
            tasks,
            files,
            Array.Empty<AnnotationLabelInfo>(),
            new Dictionary<int, IReadOnlyList<CvatRectangleAnnotation>>(),
            errors);

        // Then
        snapshot.FailedTaskCount.ShouldBe(1);
        snapshot.Tasks.Single().Error.ShouldBe("Bad annotation XML");
    }
}
