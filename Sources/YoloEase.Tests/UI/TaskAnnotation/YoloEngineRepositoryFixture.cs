using System.Security.Cryptography;
using SkiaSharp;
using Shouldly;
using YoloDotNet.Enums;
using YoloEase.UI.TaskAnnotation;

namespace YoloEase.Tests.UI.TaskAnnotation;

/// <summary>
/// Verifies that the runtime YOLO engine path can load ONNX object-detection models.
/// </summary>
public class YoloEngineRepositoryFixture
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// WHAT: Proves the CPU-backed repository can load a YOLO-style ONNX object-detection model.
    /// HOW: Uses a tiny synthetic YOLOv8 detect ONNX export with opset 17 and Ultralytics metadata.
    /// </summary>
    [Test]
    [Platform(Include = "Win")]
    public async Task ShouldLoadObjectDetectionModelAndCacheIt()
    {
        // Given
        using var repository = new YoloEngineRepository();
        var modelFile = GetModelFixture("tiny-yolov8-detect-opset17.onnx");
        var resolution = new AutoAnnotationModelResolution(modelFile, await ComputeSha256(modelFile));

        // When
        var engine = await repository.GetOrLoad(resolution).WaitAsync(Timeout);
        var cachedEngine = await repository.GetOrLoad(resolution).WaitAsync(Timeout);

        // Then
        engine.ModelType.ShouldBe(ModelType.ObjectDetection);
        engine.Labels.Select(x => x.Name).ShouldBe(new[] { "test-object", "other-object" });
        cachedEngine.ShouldBeSameAs(engine);
        repository.Engines.Count.ShouldBe(1);
    }

    /// <summary>
    /// WHAT: Proves the CPU-backed repository can run object-detection inference after loading.
    /// HOW: Runs the tiny synthetic ONNX fixture on an in-memory bitmap and verifies inference completes.
    /// </summary>
    [Test]
    [Platform(Include = "Win")]
    public async Task ShouldRunObjectDetectionInference()
    {
        // Given
        using var repository = new YoloEngineRepository();
        var modelFile = GetModelFixture("tiny-yolov8-detect-opset17.onnx");
        var resolution = new AutoAnnotationModelResolution(modelFile, await ComputeSha256(modelFile));
        var engine = await repository.GetOrLoad(resolution).WaitAsync(Timeout);

        using var bitmap = new SKBitmap(640, 640);
        bitmap.Erase(SKColors.Black);

        // When
        var detections = engine.Yolo.RunObjectDetection(bitmap, confidence: 0.25, iou: 0.7);

        // Then
        detections.ShouldNotBeNull();
    }

    private static FileInfo GetModelFixture(string fileName)
    {
        var modelFile = new FileInfo(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "TestData",
            "Models",
            fileName));
        modelFile.Exists.ShouldBeTrue($"Missing test model fixture: {modelFile.FullName}");
        return modelFile;
    }

    private static async Task<string> ComputeSha256(FileInfo file)
    {
        await using var stream = file.OpenRead();
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
