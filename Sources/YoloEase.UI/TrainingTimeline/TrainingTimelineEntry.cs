using System.Diagnostics;
using System.Globalization;
using System.Reactive.Disposables;
using System.Threading;
using Humanizer;
using PoeShared.Dialogs.Services;
using PoeShared.Logging;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TrainingTimeline;

public class TrainingTimelineEntry : RunnableTimelineEntry<TrainedModelFileInfo>
{
    private static readonly IFluentLog Log = typeof(TrainingTimelineEntry).PrepareLogger();

    private readonly TimelineController timelineController;
    private readonly Yolo8DatasetAccessor yolo8DatasetAccessor;
    private readonly Yolo8PredictAccessor predictAccessor;
    private readonly IFolderBrowserDialog folderBrowserDialog;
    private readonly IScheduler uiScheduler;

    public TrainingTimelineEntry(
        TimelineController timelineController,
        Yolo8DatasetAccessor yolo8DatasetAccessor, 
        Yolo8PredictAccessor predictAccessor, 
        DatasetInfo datasetInfo,
        IFolderBrowserDialog folderBrowserDialog,
        [Dependency(WellKnownSchedulers.UI)] IScheduler uiScheduler)
    {
        DatasetInfo = datasetInfo;
        this.timelineController = timelineController;
        this.yolo8DatasetAccessor = yolo8DatasetAccessor;
        this.predictAccessor = predictAccessor;
        this.folderBrowserDialog = folderBrowserDialog;
        this.uiScheduler = uiScheduler;
    }
    
    public DatasetInfo DatasetInfo { get; }
    
    public FileInfo ModelFile { get; private set; }
    
    public TrainedModelFileInfo TrainedModelFile { get; private set; }
    
    public async Task Predict()
    {
        uiScheduler.Schedule(async () =>
        {
            folderBrowserDialog.InitialDirectory = DatasetInfo.IndexFile.DirectoryName;
            var inputDirectory = folderBrowserDialog.ShowDialog();
            if (inputDirectory != null)
            {
                var files = inputDirectory.GetFiles();
                
                var predictEntry = new PredictTimelineEntry(new PredictArgs()
                {
                    Model = TrainedModelFile,
                    Files = files
                }, predictAccessor);
                timelineController.Add(predictEntry);
                try
                {
                    var predictInfo = await predictEntry.Run(CancellationToken.None);
                    await ProcessUtils.OpenFolder(predictInfo.OutputDirectory);
                }
                catch (Exception e)
                {
                    Log.Error("Predict error", e);   
                    predictEntry.Text = $"Predict error: {e.Message}";
                }
            }
        });
    }
    
    protected override async Task<TrainedModelFileInfo> RunInternal(CancellationToken cancellationToken)
    {
        using var progressAnchor = Disposable.Create(() => ProgressPercent = null);
        var sw = Stopwatch.StartNew();
        ProgressPercent = 0;
        Text = "Starting training process";

        try
        {
            var modelFile = await yolo8DatasetAccessor.TrainModel(DatasetInfo, update =>
            {
                ProgressPercent = (int)update.ProgressPercentage;
                Text = $"{update.ProgressPercentage:F0}% in {sw.Elapsed.Humanize(culture: CultureInfo.InvariantCulture)}, epochs: {update.EpochCurrent}/{update.EpochMax}, VideoRAM: {update.VideoRAM}";
            }, cancellationToken);

            var modelName = DatasetInfo.ProjectInfo.ModelTrainingSettings.Model;
            var projectName = string.IsNullOrEmpty(DatasetInfo.ProjectInfo.ProjectName) ? string.Empty : $"{Humanizer.InflectorExtensions.Pascalize(DatasetInfo.ProjectInfo.ProjectName)}_";
            var modelSuffix = string.IsNullOrEmpty(modelName) ? string.Empty : $"{Path.GetFileNameWithoutExtension(modelName)}_";
            var modelFilePathRevision = Path.Combine(modelFile.DirectoryName!, $"{projectName}{modelSuffix}{Path.GetFileName(DatasetInfo.IndexFile.DirectoryName)}{modelFile.Extension}");
            File.Move(modelFile.FullName, modelFilePathRevision);
            var modelFileRevision = new FileInfo(modelFilePathRevision);
            
            var resultsImage = new FileInfo(Path.Combine(modelFile.DirectoryName, "..", "results.png"));
            if (resultsImage.Exists)
            {
                Images.Add(resultsImage);
            }

            ModelFile = modelFileRevision;
            TrainedModelFile = new TrainedModelFileInfo()
            {
                ModelFile = modelFileRevision
            };
            return TrainedModelFile;
        }
        finally
        {
            ProgressPercent = null;
        }
    }
}