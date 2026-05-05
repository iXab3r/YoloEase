using Shouldly;
using YoloEase.UI.TrainingTimeline;

namespace YoloEase.Tests.UI.TrainingTimeline;

public class AutomaticTrainerPredictionFixture
{
    /// <summary>
    /// WHAT: The all-files prediction strategy should evaluate the current project-owned asset set.
    /// HOW: Resolves prediction files from separate all-file and unannotated-file lists and checks that all project files win.
    /// </summary>
    [Test]
    public void ShouldResolveAllProjectFilesForAllFilesPredictionStrategy()
    {
        // Given
        var allFiles = new[]
        {
            new FileInfo(@"C:\data\a.png"),
            new FileInfo(@"C:\data\b.png"),
            new FileInfo(@"C:\data\c.png"),
        };
        var unannotatedFiles = new[] { allFiles[2] };

        // When
        var result = AutomaticTrainer.ResolvePredictionFiles(
            AutomaticTrainerPredictionStrategy.AllFiles,
            allFiles,
            unannotatedFiles,
            100);

        // Then
        result.Select(x => x.Name).ShouldBe(new[] { "a.png", "b.png", "c.png" });
    }

    /// <summary>
    /// WHAT: The unannotated prediction strategy should limit prediction to files not already attached to annotation tasks.
    /// HOW: Resolves prediction files with a 50% sample and checks that selection comes only from the unannotated list.
    /// </summary>
    [Test]
    public void ShouldResolveUnannotatedFilesForUnlabeledPredictionStrategy()
    {
        // Given
        var allFiles = new[]
        {
            new FileInfo(@"C:\data\a.png"),
            new FileInfo(@"C:\data\b.png"),
            new FileInfo(@"C:\data\c.png"),
            new FileInfo(@"C:\data\d.png"),
        };
        var unannotatedFiles = new[] { allFiles[1], allFiles[3] };

        // When
        var result = AutomaticTrainer.ResolvePredictionFiles(
            AutomaticTrainerPredictionStrategy.Unlabeled,
            allFiles,
            unannotatedFiles,
            50);

        // Then
        result.Select(x => x.Name).ShouldBe(new[] { "b.png" });
    }

    /// <summary>
    /// WHAT: Disabled prediction strategy should not schedule any YOLO prediction work.
    /// HOW: Resolves prediction files with populated inputs and checks that the result is empty.
    /// </summary>
    [Test]
    public void ShouldResolveNoFilesWhenPredictionStrategyIsDisabled()
    {
        // Given
        var allFiles = new[] { new FileInfo(@"C:\data\a.png") };
        var unannotatedFiles = new[] { new FileInfo(@"C:\data\b.png") };

        // When
        var result = AutomaticTrainer.ResolvePredictionFiles(
            AutomaticTrainerPredictionStrategy.Disabled,
            allFiles,
            unannotatedFiles,
            100);

        // Then
        result.ShouldBeEmpty();
    }
}
