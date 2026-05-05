using System.Linq;
using System.Reactive;
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

/// <summary>
/// Orchestrates the multi-step timeline that creates datasets, trains models, predicts, and updates YOLO outputs.
/// </summary>
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

        this.WhenAnyValue(x => x.Project)
            .Where(x => x != null)
            .Select(project => Observable.Merge(
                Observable.Return(Unit.Default),
                this.WhenAnyValue(x => x.IsSelected)
                    .Where(isSelected => isSelected)
                    .ToUnit(),
                project.DataSources.InputDirectories.Connect()
                    .Skip(1)
                    .ToUnit(),
                project.RemoteProject.WhenAnyValue(x => x.CurrentUser)
                    .Where(_ => CanRefreshProject(project))
                    .ToUnit())
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Select(_ => project))
            .Switch()
            .SubscribeAsync(RefreshProjectForTrainer, Log.HandleUiException)
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

    public IObservableList<TimelineEntry> Timeline => timelineSource;

    public TimeSpan CycleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool IsSelected { get; set; }

    public bool ShowSettings { get; set; } = true;

    public bool AutoAnnotate { get; set; }
    
    public bool StopWhenDone { get; set; }
    
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

        var blacklistByFileName = blacklistedFileNames
            .Select(x => Path.GetFileNameWithoutExtension(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filteredPredictions = predictions
            .Where(x => !blacklistByFileName.Contains(Path.GetFileNameWithoutExtension(x.File.Name)))
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

    public async Task<AnnotationTaskInfo> CreateNextTask()
    {
        var batchEntry = new CreateTaskTimelineEntry(
            Project.RemoteProject,
            Project.Annotations,
            Project.TrainingBatch,
            Array.Empty<FileLabel>())
        {
            Text = "Preparing next batch...",
            AutoAnnotate = false,
        }.AddTo(timelineSource);
        
        return await batchEntry.Run(CancellationToken.None);
    }

    public async Task NavigateToNextUnannotatedTask()
    {
        var tasks = Project.TrainingBatch.UnannotatedTasks.Items.ToArray();
        if (tasks.Length <= 0)
        {
            throw new InvalidOperationException("No tasks for annotation");
        }

        var task = tasks.First();
        await Project.RemoteProject.NavigateToTask(task.Id);
    }

    public async Task Stop()
    {
        activeTrainingCancellationTokenSource.Cancel();
    }

    public async Task Start()
    {
        using var isBusy = MarkAsBusy();
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
    
    protected override async Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
        await Project.Refresh(progressReporter);
    }

    private async Task RefreshProjectForTrainer(YoloEaseProject project)
    {
        if (Project == null || !ReferenceEquals(Project, project) || IsBusy || !CanRefreshProject(project))
        {
            return;
        }

        Log.Info("Refreshing trainer project state");
        await Refresh();
    }

    private static bool CanRefreshProject(YoloEaseProject project)
    {
        return project.RemoteProject.Mode == AnnotationBackendMode.Offline ||
               project.RemoteProject.CurrentUser != null;
    }

    private async Task HandleTraining(CancellationToken cancellationToken)
    {
        var cycleIdx = 1;
        DatasetInfo lastTrainedDataset = default;
        ModelTrainingSettings lastTrainingSettings = default;
        TrainedModelFileInfo lastTrainedModel = default;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (StopWhenDone && cycleIdx > 1)
            {
                Log.Info("Stopping training as one run is already done");
                break;
            }
            
            try
            {
                
                new BasicTimelineEntry
                {
                    Text = $"Cycle #{cycleIdx} started",
                    PrefixIcon = " fa-arrow-circle-right",
                    Timestamp = clock.Now
                }.AddTo(timelineSource);

                var projectEntry = new ProjectTimelineEntry(Project).AddTo(timelineSource);
                await projectEntry.Run(cancellationToken);

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
                
                var trainingSettings = Project.TrainingDataset.TrainingSettings;
                if (lastTrainedDataset is {ProjectInfo: not null})
                {
                    var changesetEntry = new ChangesetTimelineEntry(
                        lastTrainedDataset,
                        lastTrainingSettings,
                        trainingSettings,
                        Project.Annotations, Project.RemoteProject).AddTo(timelineSource);
                    var changeset = await changesetEntry.Run(cancellationToken);
                    Log.Info($"Changeset size: {changeset.Count}");
                    if (!changeset.Any())
                    {
                        Log.Info("Skipping training cycle - no changes");
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

                if (!StopWhenDone)
                {
                    var gracePeriodEntry = new TimeoutTimelineEntry(CycleTimeout).AddTo(timelineSource);
                    await gracePeriodEntry.Run(cancellationToken);
                }
            }
        }
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
        if (directories.Length > 1)
        {
            throw new NotSupportedException("Predict is not supported for multiple input directories(yet)");
        }
        
        if (directories.Length<=0)
        {
            throw new InvalidOperationException("No input directories");
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
            new BasicTimelineEntry()
            {
                Text = "Skipping predictions - no files detected"
            }.AddTo(timelineSource);
            return;
        }
        
        var noPredictions = latestPredictions == null;
        var isAnotherModel = latestPredictions?.ModelFile != predictions.PredictionModel;
        var storageHasChanged = latestPredictions != null && !Yolo8PredictAccessor.HasAllPredictions(filesToRunPredictOn, latestPredictions);
        
        var haveToPredict = noPredictions || isAnotherModel || storageHasChanged;
        if (!haveToPredict)
        {
            new BasicTimelineEntry()
            {
                Text = $"Skipping predictions - not needed, status: {new { noPredictions, isAnotherModel, storageHasChanged }}"
            }.AddTo(timelineSource);
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
