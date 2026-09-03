using System.IO.Compression;
using System.Reactive.Disposables;
using System.Threading;
using PoeShared.Logging;
using PoeShared.UI;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TrainingTimeline;

/// <summary>
/// Timeline step that prepares a dataset package for cloud training.
/// </summary>
public class PrepareForCloudTrainingTimelineEntry : RunnableTimelineEntry<FileInfo>
{
    private static readonly IFluentLog Log = typeof(PrepareForCloudTrainingTimelineEntry).PrepareLogger();
    private static readonly HashSet<string> ImageFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp",
        ".jpeg",
        ".jpg",
        ".png",
    };

    public PrepareForCloudTrainingTimelineEntry(
        TimelineController timelineController,
        DatasetInfo datasetInfo)
    {
        DatasetInfo = datasetInfo;
    }

    public DatasetInfo DatasetInfo { get; }

    public DirectoryInfo DataArchiveDirectory { get; private set; }
    
    public FileInfo? DataArchiveFile { get; private set; }

    protected override async Task<FileInfo> RunInternal(CancellationToken cancellationToken)
    {
        using var progressAnchor = Disposable.Create(() => ProgressPercent = null);

        DataArchiveDirectory = DatasetInfo.IndexFile.Directory!;
        var changesetName = Path.GetFileName(DataArchiveDirectory.FullName);
        var outputZipPath = Path.Combine(DataArchiveDirectory.Parent!.FullName, $"{DatasetInfo.ProjectInfo.ProjectName}_{changesetName}_{DatasetInfo.ProjectInfo.ModelTrainingSettings.Model}.zip");

        Text = $"Zipping revision {changesetName}...";
            
        using var progressTracker = new ComplexProgressTracker();
        var zipProgress = progressTracker.GetOrAdd("ZIP");
        using var progressUpdater = progressTracker.WhenAnyValue(x => x.ProgressPercent)
            .Sample(UiConstants.UiThrottlingDelay)
            .Subscribe(x =>
        {
            Log.Debug($"Zipping progress: {x:F1}%");
            ProgressPercent = x;
        });

        ZipDirectory(DataArchiveDirectory.FullName, outputZipPath, zipProgress);
        
        var outputZip = new FileInfo(outputZipPath);
        DataArchiveFile = outputZip;
        Text = $"Revision {changesetName} is prepared for upload";
        return outputZip;
    }

    internal static void ZipDirectory(string sourceDirectory, string destinationZipFilePath, IProgressReporter? progressReporter)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"The specified directory '{sourceDirectory}' does not exist.");
        }

        var destinationDirectory = Path.GetDirectoryName(destinationZipFilePath);
        if (!Directory.Exists(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory!);
        }

        var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
        var temporaryZipPath = Path.Combine(
            destinationDirectory!,
            $".{Path.GetFileName(destinationZipFilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var outputStream = new FileStream(temporaryZipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(outputStream, ZipArchiveMode.Create))
            {
                for (var index = 0; index < files.Length; index++)
                {
                    var filePath = files[index];
                    var entryName = Path.GetRelativePath(sourceDirectory, filePath)
                        .Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');
                    var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);

                    using var entryStream = entry.Open();
                    using var fileStream = File.OpenRead(filePath);
                    if (ImageFileExtensions.Contains(Path.GetExtension(filePath)) && fileStream.Length == 0)
                    {
                        throw new InvalidDataException($"Image file is empty and cannot be exported: {filePath}");
                    }

                    fileStream.CopyTo(entryStream);
                    progressReporter?.Update(index + 1, files.Length);
                }
            }

            using (var validationArchive = ZipFile.OpenRead(temporaryZipPath))
            {
                if (validationArchive.Entries.Count != files.Length)
                {
                    throw new InvalidDataException(
                        $"ZIP validation failed: expected {files.Length} entries, found {validationArchive.Entries.Count}.");
                }
            }

            File.Move(temporaryZipPath, destinationZipFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryZipPath))
            {
                File.Delete(temporaryZipPath);
            }
        }

        if (!File.Exists(destinationZipFilePath))
        {
            throw new FileNotFoundException($"Failed to zip {sourceDirectory} to {destinationZipFilePath}", destinationZipFilePath);
        }
    }
}
