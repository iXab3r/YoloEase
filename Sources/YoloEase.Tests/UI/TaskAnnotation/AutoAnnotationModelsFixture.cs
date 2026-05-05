using Shouldly;
using YoloEase.UI.Dto;
using YoloEase.UI.TaskAnnotation;

namespace YoloEase.Tests.UI.TaskAnnotation;

public class AutoAnnotationModelsFixture
{
    /// <summary>
    /// WHAT: Verifies that a Latest entry stops claiming the old loaded model after a newer trained model appears.
    /// HOW: Points the entry at an old file, synchronizes it with a newer file, and checks that it is unresolved for loading again.
    /// </summary>
    [Test]
    public void ShouldRetargetLatestModelAndResetLoadedStatus()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var oldModel = CreateModelFile(temp, "old.onnx", DateTime.UtcNow.AddMinutes(-10));
        var newModel = CreateModelFile(temp, "new.onnx", DateTime.UtcNow);
        var model = AutoAnnotationModelConfig.CreateLatest();
        model.LastStatus = AutoAnnotationModelStatus.Ready;
        model.LastResolvedModelPath = oldModel.FullName;
        model.LastResolvedModelHash = "old-hash";
        model.LastResolvedModelLength = oldModel.Length;
        model.LastResolvedModelLastWriteTimeUtc = oldModel.LastWriteTimeUtc;
        model.LastRunAt = DateTimeOffset.Now;
        model.LastRunSummary = "10 detections, 0 skipped";

        // When
        var changed = AutoAnnotationAccessor.SynchronizeLatestModelReference(model, newModel);

        // Then
        changed.ShouldBeTrue();
        model.LastResolvedModelPath.ShouldBe(newModel.FullName);
        model.LastResolvedModelHash.ShouldBeNull();
        model.LastResolvedModelLength.ShouldBe(newModel.Length);
        model.LastResolvedModelLastWriteTimeUtc.ShouldBe(newModel.LastWriteTimeUtc);
        model.LastStatus.ShouldBe(AutoAnnotationModelStatus.NotChecked);
        model.LastError.ShouldBeNull();
        model.LastRunAt.ShouldBeNull();
        model.LastRunSummary.ShouldBeNull();
    }

    /// <summary>
    /// WHAT: Verifies that the latest trained model selector uses the newest existing ONNX file.
    /// HOW: Provides model records with different write times and checks the selected file.
    /// </summary>
    [Test]
    public void ShouldResolveNewestExistingTrainedModel()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var oldModel = CreateModelFile(temp, "old.onnx", DateTime.UtcNow.AddMinutes(-10));
        var newModel = CreateModelFile(temp, "new.onnx", DateTime.UtcNow);
        var missingModel = new FileInfo(Path.Combine(temp.Path, "missing.onnx"));

        // When
        var latest = AutoAnnotationAccessor.ResolveLatestTrainedModel(new[]
        {
            new TrainedModelFileInfo { ModelFile = oldModel },
            new TrainedModelFileInfo { ModelFile = missingModel },
            new TrainedModelFileInfo { ModelFile = newModel },
        });

        // Then
        latest.ShouldNotBeNull();
        latest.FullName.ShouldBe(newModel.FullName);
    }

    [Test]
    public void ShouldPreserveCreateSuggestionsInModelProperties()
    {
        // Given
        var model = AutoAnnotationModelConfig.CreateLatest();
        model.CreateSuggestions = true;

        // When
        var properties = model.ToProperties();
        var restored = AutoAnnotationModelConfig.FromProperties(properties);
        var clone = restored.CloneAsNewEntry();

        // Then
        properties.CreateSuggestions.ShouldBeTrue();
        restored.CreateSuggestions.ShouldBeTrue();
        clone.CreateSuggestions.ShouldBeTrue();
    }

    [Test]
    public void ShouldReplaceOnlyMatchingFrameSuggestionsForModel()
    {
        // Given
        var model = AutoAnnotationModelConfig.CreateLatest();
        model.Id = "model-a";
        model.DisplayName = "Model A";
        var otherModelSuggestion = new AutoAnnotationSuggestion
        {
            FrameIndex = 0,
            ModelEntryId = "model-b",
            LabelId = 1,
            BoundingBox = new RectangleD(1, 1, 10, 10),
        };
        var otherFrameSuggestion = new AutoAnnotationSuggestion
        {
            FrameIndex = 1,
            ModelEntryId = model.Id,
            LabelId = 1,
            BoundingBox = new RectangleD(2, 2, 20, 20),
        };
        var matchingSuggestion = new AutoAnnotationSuggestion
        {
            FrameIndex = 0,
            ModelEntryId = model.Id,
            LabelId = 1,
            BoundingBox = new RectangleD(3, 3, 30, 30),
        };
        var suggestions = new List<AutoAnnotationSuggestion>
        {
            otherModelSuggestion,
            otherFrameSuggestion,
            matchingSuggestion,
        };

        var frameResult = new AutoAnnotationFrameResult(
            0,
            new[]
            {
                new AutoAnnotationPrediction
                {
                    FrameIndex = 0,
                    ProjectLabelId = 7,
                    BoundingBox = new RectangleD(4, 5, 40, 50),
                    Confidence = 0.87,
                    ModelLabelIndex = 2,
                    ModelLabelName = "target",
                },
            },
            0);

        // When
        var replaced = AutoAnnotationSuggestionOperations.ReplaceFrameSuggestions(suggestions, model, frameResult);

        // Then
        replaced.ShouldBe(1);
        suggestions.ShouldContain(otherModelSuggestion);
        suggestions.ShouldContain(otherFrameSuggestion);
        suggestions.ShouldNotContain(matchingSuggestion);

        var suggestion = suggestions.Single(x => x.ModelEntryId == model.Id && x.FrameIndex == 0);
        suggestion.LabelId.ShouldBe(7);
        suggestion.BoundingBox.ShouldBe(new RectangleD(4, 5, 40, 50));
        suggestion.Confidence.ShouldBe(0.87);
        suggestion.ModelLabelName.ShouldBe("target");
    }

    [Test]
    public void ShouldClearOnlySuggestionsForModel()
    {
        // Given
        var model = AutoAnnotationModelConfig.CreateLatest();
        model.Id = "model-a";
        var modelSuggestion = new AutoAnnotationSuggestion
        {
            ModelEntryId = model.Id,
            FrameIndex = 0,
            LabelId = 1,
            BoundingBox = new RectangleD(1, 1, 10, 10),
        };
        var secondModelSuggestion = new AutoAnnotationSuggestion
        {
            ModelEntryId = model.Id,
            FrameIndex = 1,
            LabelId = 2,
            BoundingBox = new RectangleD(2, 2, 20, 20),
        };
        var otherModelSuggestion = new AutoAnnotationSuggestion
        {
            ModelEntryId = "model-b",
            FrameIndex = 0,
            LabelId = 3,
            BoundingBox = new RectangleD(3, 3, 30, 30),
        };
        var suggestions = new List<AutoAnnotationSuggestion>
        {
            modelSuggestion,
            secondModelSuggestion,
            otherModelSuggestion,
        };

        // When
        var removed = AutoAnnotationSuggestionOperations.ClearModelSuggestions(suggestions, model);

        // Then
        removed.ShouldBe(2);
        suggestions.ShouldBe(new[] { otherModelSuggestion });
    }

    private static FileInfo CreateModelFile(TemporaryDirectory temp, string fileName, DateTime lastWriteTimeUtc)
    {
        var file = new FileInfo(Path.Combine(temp.Path, fileName));
        File.WriteAllText(file.FullName, fileName);
        File.SetLastWriteTimeUtc(file.FullName, lastWriteTimeUtc);
        file.Refresh();
        return file;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "YoloEaseAutoAnnotationTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    [Test]
    public void ShouldRemoveSingleSuggestionById()
    {
        // Given
        var target = new AutoAnnotationSuggestion
        {
            Id = "suggestion-a",
            ModelEntryId = "model-a",
            FrameIndex = 0,
            LabelId = 1,
            BoundingBox = new RectangleD(1, 1, 10, 10),
        };
        var other = new AutoAnnotationSuggestion
        {
            Id = "suggestion-b",
            ModelEntryId = "model-a",
            FrameIndex = 0,
            LabelId = 2,
            BoundingBox = new RectangleD(2, 2, 20, 20),
        };
        var suggestions = new List<AutoAnnotationSuggestion>
        {
            target,
            other,
        };

        // When
        var removed = AutoAnnotationSuggestionOperations.RemoveSuggestion(suggestions, "SUGGESTION-A");

        // Then
        removed.ShouldBe(1);
        suggestions.ShouldBe(new[] { other });
    }

    [Test]
    public void ShouldMaterializeSuggestionAsManualShape()
    {
        // Given
        var suggestion = new AutoAnnotationSuggestion
        {
            FrameIndex = 3,
            LabelId = 11,
            BoundingBox = new RectangleD(10, 20, 30, 40),
            ModelEntryId = "model-a",
            Confidence = 0.91,
        };

        // When
        var shape = suggestion.ToManualShape();

        // Then
        shape.FrameIndex.ShouldBe(3);
        shape.LabelId.ShouldBe(11);
        shape.X.ShouldBe(10);
        shape.Y.ShouldBe(20);
        shape.Width.ShouldBe(30);
        shape.Height.ShouldBe(40);
        shape.Source.ShouldBe("manual");
        shape.Confidence.ShouldBeNull();
    }
}
