using System.IO.Compression;
using System.Reactive.Disposables;
using System.Threading;
using PoeShared.IO;
using PoeShared.Logging;
using PoeShared.Services;
using PoeShared.UI;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TrainingTimeline;

public class PrepareForCloudTrainingTimelineEntry : RunnableTimelineEntry<FileInfo>
{
    private static readonly IFluentLog Log = typeof(PrepareForCloudTrainingTimelineEntry).PrepareLogger();
    private readonly ISevenZipWrapper sevenZipWrapper;

    public PrepareForCloudTrainingTimelineEntry(
        TimelineController timelineController,
        DatasetInfo datasetInfo,
        ISevenZipWrapper sevenZipWrapper)
    {
        this.sevenZipWrapper = sevenZipWrapper;
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
        var outputZipPath = Path.Combine(DataArchiveDirectory.Parent!.FullName, $"{changesetName}.zip");

        Text = $"Zipping revision {changesetName}...";
            
        using var progressTracker = new ComplexProgressTracker();
        using var progressUpdater = progressTracker.WhenAnyValue(x => x.ProgressPercent)
            .Sample(UiConstants.UiThrottlingDelay)
            .Subscribe(x =>
        {
            Log.Debug($"Zipping progress: {x:F1}%");
            ProgressPercent = x;
        });

        sevenZipWrapper.CreateFromDirectory(DataArchiveDirectory, new FileInfo(outputZipPath), CompressionLevel.NoCompression);
        //ZipDirectory(DataArchiveDirectory.FullName, outputZipPath, progressTracker.GetOrAdd("zip"));
        
        var outputZip = new FileInfo(outputZipPath);
        DataArchiveFile = outputZip;
        Text = $"Revision {changesetName} is prepared for upload";
        return outputZip;
    }

    private static void ZipDirectory(string sourceDirectory, string destinationZipFilePath, IProgressReporter progressReporter)
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
        
        

        ZipFileUtils.CreateFromDirectory(new DirectoryInfo(sourceDirectory), new OSPath(destinationZipFilePath), CompressionLevel.NoCompression, progressReporter);
        if (!File.Exists(destinationZipFilePath))
        {
            throw new FileNotFoundException($"Failed to zip {sourceDirectory} to {destinationZipFilePath}", destinationZipFilePath);
        }
    }
}