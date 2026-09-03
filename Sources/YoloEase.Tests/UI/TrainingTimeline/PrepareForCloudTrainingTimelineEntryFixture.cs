using System.IO.Compression;
using Shouldly;
using YoloEase.UI.TrainingTimeline;

namespace YoloEase.Tests.UI.TrainingTimeline;

public class PrepareForCloudTrainingTimelineEntryFixture
{
    /// <summary>
    /// WHAT: Cloud training data with a .zip extension must be a standard ZIP archive that Colab can extract.
    /// HOW: Packages a representative nested dataset, checks the ZIP signature, and reads its entries with .NET's ZIP reader.
    /// </summary>
    [Test]
    public void ShouldCreateStandardZipArchiveForCloudTraining()
    {
        // Given
        var testRoot = Path.Combine(Path.GetTempPath(), "YoloEaseTests", Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(testRoot, "dataset");
        var imagesDirectory = Path.Combine(sourceDirectory, "train", "images");
        var nestedDirectory = Path.Combine(sourceDirectory, "train", "labels");
        var archivePath = Path.Combine(testRoot, "project_revision_yolo11s.pt.zip");

        try
        {
            Directory.CreateDirectory(imagesDirectory);
            Directory.CreateDirectory(nestedDirectory);
            File.WriteAllText(Path.Combine(sourceDirectory, "data.yaml"), "path: .");
            File.WriteAllText(Path.Combine(imagesDirectory, "sample #1.png"), "image-content");
            File.WriteAllText(Path.Combine(nestedDirectory, "sample #1.txt"), "0 0.5 0.5 1 1");
            File.WriteAllText(archivePath, "stale archive");

            // When
            PrepareForCloudTrainingTimelineEntry.ZipDirectory(sourceDirectory, archivePath, progressReporter: null);

            // Then
            using (var archiveStream = File.OpenRead(archivePath))
            {
                var signature = new byte[4];
                archiveStream.ReadExactly(signature);
                signature.ShouldBe(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
            }

            using var archive = ZipFile.OpenRead(archivePath);
            archive.Entries.Select(x => x.FullName).ShouldBe(new[]
            {
                "data.yaml",
                "train/images/sample #1.png",
                "train/labels/sample #1.txt",
            }, ignoreOrder: true);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// WHAT: A failed cloud export must not replace an existing archive with a partial ZIP.
    /// HOW: Attempts to package an empty image and verifies that the previous archive remains untouched.
    /// </summary>
    [Test]
    public void ShouldPreserveExistingArchiveWhenDatasetContainsEmptyImage()
    {
        // Given
        var testRoot = Path.Combine(Path.GetTempPath(), "YoloEaseTests", Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(testRoot, "dataset");
        var imagesDirectory = Path.Combine(sourceDirectory, "train", "images");
        var archivePath = Path.Combine(testRoot, "project_revision_yolo11s.pt.zip");

        try
        {
            Directory.CreateDirectory(imagesDirectory);
            File.WriteAllBytes(Path.Combine(imagesDirectory, "empty.png"), Array.Empty<byte>());
            File.WriteAllText(archivePath, "previous archive");

            // When
            var exception = Should.Throw<InvalidDataException>(() =>
                PrepareForCloudTrainingTimelineEntry.ZipDirectory(sourceDirectory, archivePath, progressReporter: null));

            // Then
            exception.Message.ShouldContain("Image file is empty");
            File.ReadAllText(archivePath).ShouldBe("previous archive");
            Directory.GetFiles(testRoot, "*.tmp").ShouldBeEmpty();
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}
