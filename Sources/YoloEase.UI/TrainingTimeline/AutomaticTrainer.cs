using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using AntDesign;
using PoeShared;
using PoeShared.Common;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;
using YoloEase.UI.Yolo;

namespace YoloEase.UI.TrainingTimeline;

public class AutomaticTrainer : RefreshableReactiveObject, ICanBeSelected
{
    private static readonly Binder<AutomaticTrainer> Binder = new();

    private readonly IClock clock;
    private readonly IFactory<UpdateYoloTimelineEntry, TimelineController> updateYoloEntryFactory;
    private readonly IFactory<PrepareForCloudTrainingTimelineEntry, TimelineController, DatasetInfo> prepareForCloudTrainingTimelineEntryFactory;
    private readonly IFactory<PreTrainingTimelineEntry, TimelineController, DatasetInfo, Yolo8DatasetAccessor> preTrainingEntryFactory;
    private readonly IFactory<TrainingTimelineEntry, TimelineController, DatasetInfo, Yolo8DatasetAccessor, Yolo8PredictAccessor> trainingEntryFactory;
    private readonly IFactory<PredictTimelineEntry, TimelineController, TrainedModelFileInfo, DirectoryInfo, Yolo8DatasetAccessor, Yolo8PredictAccessor> predictEntryFactory;
    private readonly CircularSourceList<TimelineEntry> timelineSource = new(100);
    private readonly TimelineController timelineController;
    private CancellationTokenSource activeTrainingCancellationTokenSource;

    static AutomaticTrainer()
    {
        Binder.BindAction(x => x.RecalculateAutoAnnotationStats(x.AutoAnnotateConfidenceThresholdPercentage / 100f, x.Project != null ? x.Project.Predictions.LatestPredictions : null));
    }

    public AutomaticTrainer(
        IClock clock,
        IFactory<UpdateYoloTimelineEntry, TimelineController> updateYoloEntryFactory,
        IFactory<PrepareForCloudTrainingTimelineEntry, TimelineController, DatasetInfo> prepareForCloudTrainingTimelineEntryFactory,
        IFactory<PreTrainingTimelineEntry, TimelineController, DatasetInfo, Yolo8DatasetAccessor> preTrainingEntryFactory,
        IFactory<TrainingTimelineEntry, TimelineController, DatasetInfo, Yolo8DatasetAccessor, Yolo8PredictAccessor> trainingEntryFactory,
        IFactory<PredictTimelineEntry, TimelineController, TrainedModelFileInfo, DirectoryInfo, Yolo8DatasetAccessor, Yolo8PredictAccessor> predictEntryFactory)
    {
        timelineController = new TimelineController(timelineSource);
        this.clock = clock;
        this.updateYoloEntryFactory = updateYoloEntryFactory;
        this.prepareForCloudTrainingTimelineEntryFactory = prepareForCloudTrainingTimelineEntryFactory;
        this.preTrainingEntryFactory = preTrainingEntryFactory;
        this.trainingEntryFactory = trainingEntryFactory;
        this.predictEntryFactory = predictEntryFactory;

        this.WhenAnyValue(x => x.IsSelected)
            .Where(x => x)
            .Take(1)
            .SubscribeAsync(async () =>
            {
                try
                {
                    if (Project == null)
                    {
                        return;
                    }
                    await Project.Refresh();
                }
                catch (Exception e)
                {
                    WhenNotified.OnNext(new NotificationConfig()
                    {
                        NotificationType = NotificationType.Error,
                        Message = $"Error: {e.Message}",
                        Placement = NotificationPlacement.TopRight,
                    });
                }
            })
            .AddTo(Anchors);

        this.WhenAnyValue(x => x.Project)
            .SubscribeAsync(x => Stop())
            .AddTo(Anchors);

        Binder.Attach(this).AddTo(Anchors);
    }

    public YoloEaseProject Project { get; set; }

    public IObservableListEx<TimelineEntry> Timeline => timelineSource;

    public TimeSpan CycleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool IsSelected { get; set; }

    public bool AutoAnnotate { get; set; }

    public AutomaticTrainerModelStrategy ModelStrategy { get; set; }
    
    public AutomaticTrainerPickStrategy PickStrategy { get; set; }

    public AutomaticTrainerMode TrainingMode { get; set; }

    public float AutoAnnotateConfidenceThresholdPercentage { get; set; }

    public float AutoAnnotateUnannotatedFilesCount { get; private set; }

    public float AutoAnnotateTotalFilesCount { get; private set; }

    public AutomaticTrainerTaskFilter TaskFilter { get; set; } = AutomaticTrainerTaskFilter.Unannotated;

    private void RecalculateAutoAnnotationStats(float confidence, DatasetPredictInfo datasetPredictions)
    {
        if (datasetPredictions == null)
        {
            AutoAnnotateTotalFilesCount = 0;
            AutoAnnotateUnannotatedFilesCount = 0;
            return;
        }

        var predictions = datasetPredictions.Predictions
            .Select(x => x with
            {
                Labels = x.Labels.EmptyIfNull().Where(y => y.Score >= confidence).ToArray()
            })
            .Where(x => x.Labels.Any())
            .ToDictionary(x => x.File.Name);
        var unannotatedFiles = this.Project.TrainingBatch.UnannotatedFiles.Items.ToArray();
        var files = unannotatedFiles
            .Select(x =>
            {
                var predictionsForFile = predictions.GetValueOrDefault(x.Name);
                return new {File = x, Labels = predictionsForFile?.Labels ?? Array.Empty<YoloPrediction>(), Score = predictionsForFile?.Labels.Max(y => y.Score)};
            })
            .OrderByDescending(x => x.Score)
            .ToArray();
        AutoAnnotateTotalFilesCount = files.Count();
        AutoAnnotateUnannotatedFilesCount = files.Count(x => x.Score != null);
    }

    public async Task ClearTimeline()
    {
        timelineSource.Clear();
    }

    public async Task CreateNextTask()
    {
        var batchEntry = new CreateTaskTimelineEntry(
            Project.RemoteProject,
            Project.Annotations,
            Project.TrainingBatch,
            Project.Predictions.LatestPredictions)
        {
            Text = "Preparing next batch...",
            AutoAnnotate = AutoAnnotate,
            AutoAnnotateConfidenceThreshold = AutoAnnotateConfidenceThresholdPercentage / 100,
        }.AddTo(timelineSource);
        
        var taskId = await batchEntry.Run(CancellationToken.None);
        if (AutoAnnotate && batchEntry.Annotations is {Shapes.Count: 0})
        {
            WhenNotified.OnNext(new NotificationConfig(){
                NotificationType = NotificationType.Warning,
                Duration = 10000,
                Message = "Auto-annotation is enabled, but we could not place even one label. Either lower Confidence Threshold or just do some manual cycles till you'll get a better model"
            });
        }
    }

    public async Task NavigateToNextUnannotatedTask()
    {
        var tasks = Project.TrainingBatch.UnannotatedTasks.Items.ToArray();
        if (tasks.Length <= 0)
        {
            throw new InvalidOperationException("No tasks for annotation");
        }

        var task = tasks.First();
        await Project.RemoteProject.NavigateToTask(task.Id.Value);
    }

    public async Task Stop()
    {
        activeTrainingCancellationTokenSource.Cancel();
    }

    public async Task Start()
    {
        using var isBusy = isBusyLatch.Rent();
        using var cleanup = Disposable.Create(() => activeTrainingCancellationTokenSource = null);
        activeTrainingCancellationTokenSource = new CancellationTokenSource();

        timelineSource.Add(new BasicTimelineEntry
        {
            Text = "Automatic training started",
            Timestamp = clock.Now
        });

        await Task.Run(() => HandleTraining(activeTrainingCancellationTokenSource.Token));

        timelineSource.Add(new BasicTimelineEntry
        {
            Text = "Automatic training stopped",
            Timestamp = clock.Now
        });
    }

    private async Task HandleTraining(CancellationToken cancellationToken)
    {
        Project.Predictions.LatestPredictions = null;

        var cycleIdx = 1;
        DatasetInfo lastTrainedDataset = default;
        ModelTrainingSettings lastTrainingSettings = default;
        TrainedModelFileInfo lastTrainedModel = default;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                new BasicTimelineEntry
                {
                    Text = $"Cycle #{cycleIdx} started",
                    PrefixIcon = " fa-arrow-circle-o-right",
                    Timestamp = clock.Now
                }.AddTo(timelineSource);

                await Project.FileSystemAssets.Refresh();
                await Project.Assets.Refresh();

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await Project.RemoteProject.Refresh();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await Project.TrainingBatch.Refresh();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await Project.Annotations.Refresh();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (AutoAnnotate)
                {
                    await AddPredictionsIfNeeded(cancellationToken);
                }

                var trainingSettings = Project.TrainingDataset.TrainingSettings;
                if (lastTrainedDataset is {ProjectInfo: not null})
                {
                    var changesetEntry = new ChangesetTimelineEntry(
                        lastTrainedDataset,
                        lastTrainingSettings,
                        trainingSettings,
                        Project.Annotations, Project.RemoteProject).AddTo(timelineSource);
                    var changeset = await changesetEntry.Run(cancellationToken);
                    if (!changeset.Any())
                    {
                        continue;
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                    
                {
                    break;
                }

                var createDatasetEntry = new CreateDatasetTimelineEntry(Project.Annotations).AddTo(timelineSource);
                var datasetInfo = await createDatasetEntry.Run(cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (TrainingMode == AutomaticTrainerMode.GoogleColab)
                {
                    var prepareForCloudEntry = prepareForCloudTrainingTimelineEntryFactory.Create(timelineController, datasetInfo).AddTo(timelineSource);
                    var cloudArchive = await prepareForCloudEntry.Run(cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
                else
                {
                    if (timelineController.PerformUpdateOnNextCycle)
                    {
                        var updateYoloEntry = updateYoloEntryFactory.Create(timelineController).AddTo(timelineSource);
                        await updateYoloEntry.Run(cancellationToken);
                        timelineController.PerformUpdateOnNextCycle = false;
                    }
                    
                    var preTrainingProgressEntry = preTrainingEntryFactory.Create(timelineController, datasetInfo, Project.TrainingDataset).AddTo(timelineSource);
                    await preTrainingProgressEntry.Run(cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }


                    try
                    {
                        var trainingProgressEntry = trainingEntryFactory.Create(timelineController, datasetInfo, Project.TrainingDataset, Project.Predictions).AddTo(timelineSource);
                        var modelFile = await trainingProgressEntry.Run(cancellationToken);
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        if (ModelStrategy == AutomaticTrainerModelStrategy.Latest)
                        {
                            Project.Predictions.PredictionModel = modelFile;
                            await AddPredictionsIfNeeded(cancellationToken);
                        }
                    }
                    catch (Exception e)
                    {
                        if (e.Message.Contains("New Ultralytics Yolo8 version detected", StringComparison.OrdinalIgnoreCase))
                        {
                            var updateYoloEntry = updateYoloEntryFactory.Create(timelineController).AddTo(timelineSource);
                            updateYoloEntry.UpdateText = e.Message;
                            await updateYoloEntry.Run(cancellationToken);
                            continue;
                        }

                        throw;
                    }
                }

                lastTrainingSettings = trainingSettings;
                lastTrainedDataset = datasetInfo;
            }
            catch (Exception e)
            {
                Log.Error("Timeline error", e);
                new ErrorTimelineEntry(e).AddTo(timelineSource);
            }
            finally
            {
                new BasicTimelineEntry
                {
                    Text = $"Cycle #{cycleIdx} completed",
                    PrefixIcon = " fa-arrow-circle-o-left",
                    Timestamp = clock.Now
                }.AddTo(timelineSource);
                cycleIdx++;

                var gracePeriodEntry = new TimeoutTimelineEntry(CycleTimeout).AddTo(timelineSource);
                await gracePeriodEntry.Run(cancellationToken);
            }
        }

        cancellationToken.WaitHandle.WaitOne();
    }

    private async Task AddPredictionsIfNeeded(CancellationToken cancellationToken)
    {
        if (Project.Predictions.PredictionModel == null)
        {
            return;
        }

        var directories = Project.Assets.InputDirectories.Items.ToArray();
        if (directories.Count() > 1)
        {
            throw new NotSupportedException("Predict is not supported for multiple input directories(yet)");
        }

        var inputDirectory = directories[0];

        var noPredictions = Project.Predictions.LatestPredictions == null;
        var isAnotherModel = Project.Predictions.LatestPredictions?.ModelFile != Project.Predictions.PredictionModel;
        var storageHasChanged = Project.Predictions.LatestPredictions != null && !Yolo8PredictAccessor.HasAllPredictions(inputDirectory, Project.Predictions.LatestPredictions);
        var haveToPredict = noPredictions || isAnotherModel || storageHasChanged;
        if (!haveToPredict)
        {
            return;
        }

        var predictEntry = predictEntryFactory.Create(timelineController, Project.Predictions.PredictionModel, inputDirectory, Project.TrainingDataset, Project.Predictions).AddTo(timelineSource);
        var predictions = await predictEntry.Run(cancellationToken);
        Project.Predictions.LatestPredictions = predictions;
    }
}