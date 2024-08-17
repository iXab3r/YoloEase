using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using AntDesign;
using PoeShared;
using PoeShared.Blazor.Controls;
using PoeShared.Common;
using PoeShared.UI;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TrainingTimeline;

public class AutomaticTrainer : RefreshableReactiveObject, ICanBeSelected
{
    private static readonly Binder<AutomaticTrainer> Binder = new();

    private readonly IClock clock;
    private readonly IFactory<UpdateYoloTimelineEntry, TimelineController> updateYoloEntryFactory;
    private readonly IFactory<PrepareForCloudTrainingTimelineEntry, TimelineController, DatasetInfo> prepareForCloudTrainingTimelineEntryFactory;
    private readonly IFactory<PreTrainingTimelineEntry, TimelineController, DatasetInfo, Yolo8DatasetAccessor> preTrainingEntryFactory;
    private readonly IFactory<TrainingTimelineEntry, TimelineController, DatasetInfo, Yolo8DatasetAccessor, Yolo8PredictAccessor> trainingEntryFactory;
    private readonly IFactory<PredictTimelineEntry, TimelineController, PredictArgs, Yolo8DatasetAccessor, Yolo8PredictAccessor> predictEntryFactory;
    private readonly CircularSourceList<TimelineEntry> timelineSource = new(100);
    private readonly TimelineController timelineController;

    private CancellationTokenSource activeTrainingCancellationTokenSource = new();

    static AutomaticTrainer()
    {
    }

    public AutomaticTrainer(
        IClock clock,
        IFactory<UpdateYoloTimelineEntry, TimelineController> updateYoloEntryFactory,
        IFactory<PrepareForCloudTrainingTimelineEntry, TimelineController, DatasetInfo> prepareForCloudTrainingTimelineEntryFactory,
        IFactory<PreTrainingTimelineEntry, TimelineController, DatasetInfo, Yolo8DatasetAccessor> preTrainingEntryFactory,
        IFactory<TrainingTimelineEntry, TimelineController, DatasetInfo, Yolo8DatasetAccessor, Yolo8PredictAccessor> trainingEntryFactory,
        IFactory<PredictTimelineEntry, TimelineController, PredictArgs, Yolo8DatasetAccessor, Yolo8PredictAccessor> predictEntryFactory)
    {
        timelineController = new TimelineController(timelineSource);
        this.clock = clock;
        this.updateYoloEntryFactory = updateYoloEntryFactory;
        this.prepareForCloudTrainingTimelineEntryFactory = prepareForCloudTrainingTimelineEntryFactory;
        this.preTrainingEntryFactory = preTrainingEntryFactory;
        this.trainingEntryFactory = trainingEntryFactory;
        this.predictEntryFactory = predictEntryFactory;

        this.WhenAnyValue(x => x.Project)
            .SubscribeAsync(x => Stop(), Log.HandleUiException)
            .AddTo(Anchors);

        Observable.CombineLatest(
                this.WhenAnyValue(x => x.PredictIncludeAnnotated),
                this.WhenAnyValue(x => x.Project.Predictions.LatestPredictions),
                this.WhenAnyValue(x => x.Project.RemoteProject.ProjectFiles).Switch().Select(x =>
                {
                    var project = Project;
                    if (project == null)
                    {
                        return new string[0];
                    }
                    return project.RemoteProject.ProjectFiles.Items.Select(y => y.FileName).ToArray();
                }),
                (includeAnnotated, predictions, projectFileNames) => new { includeAnnotated, predictions, projectFileNames })
            .Throttle(UiConstants.UiThrottlingDelay)
            .Subscribe(x => RecalculatePredictItems(x.predictions, x.includeAnnotated ? new string[0] : x.projectFileNames), Log.HandleUiException)
            .AddTo(Anchors);

        Binder.Attach(this).AddTo(Anchors);
    }

    public YoloEaseProject Project { get; set; }

    public IObservableListEx<TimelineEntry> Timeline => timelineSource;

    public TimeSpan CycleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool IsSelected { get; set; }

    public bool ShowSettings { get; set; } = true;

    public bool AutoAnnotate { get; set; }
    
    public bool PredictIncludeAnnotated { get; set; }

    public AutomaticTrainerModelStrategy ModelStrategy { get; set; }
    
    public AutomaticTrainerPredictionStrategy PredictionStrategy { get; set; }
    
    public AutomaticTrainerFilePickStrategy PickStrategy { get; set; }
    
    public AutomaticTrainerAutoAnnotateThresholdStrategy AutoAnnotateThresholdStrategy { get; set; }

    public AutomaticTrainerMode TrainingMode { get; set; }

    public float PredictBatchPercentage { get; set; } = 100;
    
    public AutomaticTrainerTaskFilter TaskFilter { get; set; } = AutomaticTrainerTaskFilter.Unannotated;

    public ISourceList<PredictLabelItem> PredictItems { get; } = new SourceList<PredictLabelItem>();
    
    public float AutoAnnotateConfidenceThresholdPercentage { get; set; }

    private void RecalculatePredictItems(
        DatasetPredictInfo datasetPredict, 
        IEnumerable<string> blacklistedFileNames)
    {
        var predictions = datasetPredict != null && datasetPredict.Predictions != null
            ? datasetPredict.Predictions
            : new PredictInfo[0];

        var blacklistByFileName = blacklistedFileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filteredPredictions = predictions
            .Where(x => !blacklistByFileName.Contains(x.File.Name))
            .ToArray();
        
        var blacklistedPrediction = predictions
            .Where(x => blacklistByFileName.Contains(x.File.Name))
            .ToArray();
        
        var labelTypes = filteredPredictions
            .SelectMany(x => x.Labels)
            .Select(x => x.Label)
            .Distinct()
            .ToArray();
        
        var labelsToShow = labelTypes
            .Select(x => PredictLabelItem.FromPredicts(x.Id, filteredPredictions))
            .ToDictionary(x => x.Label);
        
        var labelsToProcess = PredictItems
            .Items.Select(x => x.Label)
            .Concat(labelsToShow.Values.Select(x => x.Label))
            .DistinctBy(x => x.Id)
            .ToArray();


        foreach (var label in labelsToProcess)
        {
            var labels = labelsToShow.GetOrDefault(label)?.Labels ?? new FileLabel[0];
            
            var existingLabelItem = PredictItems.Items.FirstOrDefault(x => x.Label.Id == label.Id);
            if (existingLabelItem == null)
            {
                PredictItems.Add(new PredictLabelItem(labels));
            }
            else
            {
                existingLabelItem.UpdateLabels(labels);
            }
        }
    }

    public async Task ClearTimeline()
    {
        timelineSource.Clear();
    }

    public async Task CreateNextTask()
    {
        var labeledFiles = AutoAnnotateThresholdStrategy switch
        {
            AutomaticTrainerAutoAnnotateThresholdStrategy.Global => PredictItems.Items.SelectMany(x => x.EnumerateLabels(AutoAnnotateConfidenceThresholdPercentage / 100)).ToArray(),
            AutomaticTrainerAutoAnnotateThresholdStrategy.PerLabel => PredictItems.Items.SelectMany(x => x.EnumerateLabels(x.ScoreThreshold)).ToArray(),
            _ => throw new ArgumentOutOfRangeException()
        };
        
        var batchEntry = new CreateTaskTimelineEntry(
            Project.RemoteProject,
            Project.Annotations,
            Project.TrainingBatch,
            labeledFiles)
        {
            Text = "Preparing next batch...",
            AutoAnnotate = AutoAnnotate,
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
                    PrefixIcon = " fa-arrow-circle-right",
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

                if (Project.Predictions.PredictionModel == null && ModelStrategy == AutomaticTrainerModelStrategy.Latest)
                {
                    if (Project.TrainingDataset.TrainedModels.Count <= 0)
                    {
                        new BasicTimelineEntry()
                        {
                            Text = $"Model is not set, refreshing trained models list",
                            Timestamp = clock.Now
                        }.AddTo(timelineSource);
                        await Project.TrainingDataset.Refresh();
                    }
                    
                    if (Project.TrainingDataset.TrainedModels.Count > 0)
                    {
                        var modelToPick = Project.TrainingDataset.TrainedModels.Items
                            .OrderByDescending(x => x.ModelFile.LastWriteTime)
                            .First();
                        Project.Predictions.PredictionModel = modelToPick;
                        new BasicTimelineEntry
                        {
                            Text = $"Latest model: {modelToPick}",
                            Timestamp = clock.Now
                        }.AddTo(timelineSource);
                    }
                    else
                    {
                        new BasicTimelineEntry
                        {
                            Text = $"Latest model: could not find any models at all",
                            Timestamp = clock.Now
                        }.AddTo(timelineSource);
                    }
                }
                
                await Project.TrainingDataset.Refresh();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                
                await AddPredictionsIfNeeded(cancellationToken);

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

                var createDatasetEntry = new CreateDatasetTimelineEntry(Project.Annotations, Project.Augmentations).AddTo(timelineSource);
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
                        
                        if (ModelStrategy == AutomaticTrainerModelStrategy.Latest)
                        {
                            Project.Predictions.PredictionModel = modelFile;
                        }
                        
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
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
                    PrefixIcon = " fa-arrow-circle-left",
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
        var predictions = Project.Predictions;
        var predictionModel = predictions.PredictionModel;
        if (predictionModel == null)
        {
            return;
        }

        var directories = Project.Assets.InputDirectories.Items.ToArray();
        if (directories.Count() > 1)
        {
            throw new NotSupportedException("Predict is not supported for multiple input directories(yet)");
        }

        var inputDirectory = directories[0];

        var latestPredictions = predictions.LatestPredictions;


        IReadOnlyList<FileInfo> filesToRunPredictOn;
        switch (PredictionStrategy)
        {
            case AutomaticTrainerPredictionStrategy.AllFiles:
            {
                filesToRunPredictOn = inputDirectory.GetFiles().PickPercentage(PredictBatchPercentage);
                break;
            }
            case AutomaticTrainerPredictionStrategy.Disabled:
            {
                filesToRunPredictOn = [];
                break;
            }
            case AutomaticTrainerPredictionStrategy.Unlabeled:
            {
                filesToRunPredictOn = Project.TrainingBatch.UnannotatedFiles.Items.ToArray().PickPercentage(PredictBatchPercentage);;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (filesToRunPredictOn.IsEmpty())
        {
            return;
        }
        
        var noPredictions = latestPredictions == null;
        var isAnotherModel = latestPredictions?.ModelFile != predictions.PredictionModel;
        var storageHasChanged = latestPredictions != null && !Yolo8PredictAccessor.HasAllPredictions(filesToRunPredictOn, latestPredictions);
        
        var haveToPredict = noPredictions || isAnotherModel || storageHasChanged;
        if (!haveToPredict)
        {
            return;
        }

        var predictEntry = predictEntryFactory.Create(
            timelineController, 
            new PredictArgs()
            {
                Files = filesToRunPredictOn.ToArray(),
                Model = predictionModel
            },
            Project.TrainingDataset, 
            Project.Predictions).AddTo(timelineSource);
        var actualPredictions = await predictEntry.Run(cancellationToken);
        Project.Predictions.LatestPredictions = actualPredictions;
    }
}