using System.Runtime.InteropServices.WindowsRuntime;
using PoeShared.Scaffolding;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using YoloEase.UI.Services;

namespace YoloEase.Tests.UI.Services;

/// <summary>
/// Covers frame selection and Windows media frame extraction behavior.
/// </summary>
public class VideoFrameExtractorFixture
{
    /// <summary>
    /// WHAT: Verifies that the extractor honors the selected range and every-Nth-frame setting.
    /// HOW: Selects frames from the middle of a synthetic ten-frame video index range.
    /// </summary>
    [Test]
    public void ShouldSelectOnlyRequestedRange()
    {
        // Given
        const long frameCount = 10;

        // When
        var frameIndexes = VideoFrameSelection.SelectIndexes(frameCount, 2, 8, 3);

        // Then
        frameIndexes.ShouldBe(new long[] {2, 5, 8});
        VideoFrameSelection.GetExpectedFrameCount(frameCount, 2, 8, 3).ShouldBe(3);
    }

    /// <summary>
    /// WHAT: Verifies that invalid extraction settings are clamped to a usable range.
    /// HOW: Requests a negative start, oversized end, and zero frame interval.
    /// </summary>
    [Test]
    public void ShouldClampSelectionRangeAndFrameStep()
    {
        // Given
        const long frameCount = 5;

        // When
        var frameIndexes = VideoFrameSelection.SelectIndexes(frameCount, -10, 99, 0);

        // Then
        frameIndexes.ShouldBe(new long[] {0, 1, 2, 3, 4});
        VideoFrameSelection.GetExpectedFrameCount(frameCount, -10, 99, 0).ShouldBe(5);
    }

    /// <summary>
    /// WHAT: Verifies durable output names used when extracted frames become data-source files.
    /// HOW: Builds a frame path for a representative video file and frame index.
    /// </summary>
    [Test]
    public void ShouldBuildStableFrameOutputPath()
    {
        // Given
        var inputFile = new FileInfo(Path.Combine(Path.GetTempPath(), "camera clip.mp4"));
        var outputDirectory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "YoloEaseVideoFrameTests"));

        // When
        var frameFilePath = VideoFrameSelection.GetFrameFilePath(inputFile, outputDirectory, 42);

        // Then
        frameFilePath.ShouldBe(Path.Combine(outputDirectory.FullName, "camera clip #42.png"));
    }

    /// <summary>
    /// WHAT: Verifies timestamp clamping near the end of the video.
    /// HOW: Converts a frame index past the duration and checks that the timestamp remains inside the clip.
    /// </summary>
    [Test]
    public void ShouldClampFrameTimestampInsideDuration()
    {
        // Given
        var duration = TimeSpan.FromSeconds(1);

        // When
        var timestamp = VideoFrameSelection.GetFrameTimestamp(30, 30, duration);

        // Then
        timestamp.ShouldBeLessThan(duration);
        timestamp.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    /// <summary>
    /// WHAT: Verifies the Windows media extractor can create PNG frames and skip existing outputs.
    /// HOW: Renders a tiny temp MP4, pre-creates one output PNG, extracts frames, and loads every PNG with ImageSharp.
    /// </summary>
    [Test]
    [Platform(Include = "Win")]
    public async Task ShouldExtractVideoFramesAndSkipExistingOutputs()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var inputFile = await CreateSolidColorVideoAsync(temp.Path);
        var outputDirectory = new DirectoryInfo(Path.Combine(temp.Path, "frames"));
        var extractor = new WindowsMediaVideoFrameExtractor();
        var progressReporter = new SimpleProgressReporter();
        var probe = await extractor.ProbeAsync(inputFile);
        var endFrameIndex = Math.Min(2, probe.FrameCount - 1);
        var selectedFrameIndexes = VideoFrameSelection.SelectIndexes(probe.FrameCount, 0, endFrameIndex, 1);
        selectedFrameIndexes.Count.ShouldBeGreaterThan(0);

        Directory.CreateDirectory(outputDirectory.FullName);
        var preexistingFilePath = VideoFrameSelection.GetFrameFilePath(inputFile, outputDirectory, selectedFrameIndexes[0]);
        using (var preexistingImage = new Image<Rgba32>(1, 1))
        {
            await preexistingImage.SaveAsPngAsync(preexistingFilePath);
        }

        // When
        var result = await extractor.ExtractAsync(
            new VideoFrameExtractionRequest(inputFile, outputDirectory, 0, endFrameIndex, 1),
            progressReporter);

        // Then
        result.SelectedFrameCount.ShouldBe(selectedFrameIndexes.Count);
        result.SkippedExistingFrameCount.ShouldBe(1);
        result.SavedFrameCount.ShouldBe(selectedFrameIndexes.Count - 1);
        progressReporter.ProgressPercent.ShouldBe(100);

        var outputFiles = outputDirectory.GetFiles("*.png").OrderBy(x => x.Name).ToArray();
        outputFiles.Length.ShouldBe(selectedFrameIndexes.Count);
        foreach (var outputFile in outputFiles)
        {
            using var image = await Image.LoadAsync(outputFile.FullName);
            image.Width.ShouldBeGreaterThan(0);
            image.Height.ShouldBeGreaterThan(0);
        }
    }

    private static async Task<FileInfo> CreateSolidColorVideoAsync(string directoryPath)
    {
        var storageFolder = await StorageFolder.GetFolderFromPathAsync(directoryPath).AsTask();
        var storageFile = await storageFolder.CreateFileAsync("sample.mp4", CreationCollisionOption.ReplaceExisting).AsTask();

        var composition = new MediaComposition();
        composition.Clips.Add(MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 32, 96, 160), TimeSpan.FromSeconds(1)));

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Qvga);
        var failureReason = await composition
            .RenderToFileAsync(storageFile, MediaTrimmingPreference.Precise, profile)
            .AsTask();
        failureReason.ShouldBe(TranscodeFailureReason.None);

        return new FileInfo(storageFile.Path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "YoloEaseVideoFrameTests", Guid.NewGuid().ToString("N"));
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
}
