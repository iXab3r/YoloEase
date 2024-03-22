using System.IO.Compression;
using System.Reactive.Disposables;
using System.Threading;
using PoeShared.Logging;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TrainingTimeline;

public class PrepareForCloudTrainingTimelineEntry : RunnableTimelineEntry<FileInfo>
{
    private static readonly IFluentLog Log = typeof(PrepareForCloudTrainingTimelineEntry).PrepareLogger();

    public PrepareForCloudTrainingTimelineEntry(
        TimelineController timelineController,
        DatasetInfo datasetInfo)
    {
        DatasetInfo = datasetInfo;
    }

    public DatasetInfo DatasetInfo { get; }

    public FileInfo DataArchiveFile { get; private set; }

    protected override async Task<FileInfo> RunInternal(CancellationToken cancellationToken)
    {
        using var progressAnchor = Disposable.Create(() => ProgressPercent = null);

        var directoryPath = DatasetInfo.IndexFile.Directory!;
        var changesetName = Path.GetFileName(directoryPath.FullName);
        var outputZipPath = Path.Combine(directoryPath.Parent!.FullName, $"{changesetName}.zip");

        Text = $"Zipping revision {changesetName}...";
        ZipDirectory(directoryPath.FullName, outputZipPath);
        Text = $"Zipping revision {changesetName}...";
        var outputZip = new FileInfo(outputZipPath);
        DataArchiveFile = outputZip;
        Text = $"Revision {changesetName} is prepared for upload";
        return outputZip;
    }

    private static void ZipDirectory(string sourceDirectory, string destinationZipFilePath)
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

        if (File.Exists(destinationZipFilePath))
        {
            File.Delete(destinationZipFilePath);
        }

        ZipFile.CreateFromDirectory(sourceDirectory, destinationZipFilePath!);
        
        if (!File.Exists(destinationZipFilePath))
        {
            throw new FileNotFoundException($"Failed to zip {sourceDirectory} to {destinationZipFilePath}", destinationZipFilePath);
        }
    }
}