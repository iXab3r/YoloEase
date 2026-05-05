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

    private CancellationTokenSource? activeTrainingCancellationTokenSource = new();

    static AutomaticTrainer()
    {
        Binder.Bind(x => x.Project == null ? 0 : x.Project.Assets.Files.Count).To(x => x.ProjectFileCount);
        Binder.Bind(x => x.Project == null ? 0 : x.Project.TrainingBatch.UnannotatedFiles.Count).To(x => x.UnannotatedFileCount);
        Binder.Bind(x => Math.Max(0, x.ProjectFileCount - x.UnannotatedFileCount)).To(x => x.AnnotatedFileCount);
        Binder.Bind(x => x.Project == null ? 0 : x.Project.RemoteProject.Tasks.Count).To(x => x.ProjectTaskCount);
        Binder.Bind(x => x.Project == null ? 0 : x.Project.TrainingBatch.UnannotatedTasks.Count).To(x => x.UnannotatedTaskCount);
        Binder.Bind(x => Math.Max(0, x.ProjectTaskCount - x.UnannotatedTaskCount)).To(x => x.AnnotatedTaskCount);
        Binder.Bind(x => x.UnannotatedFileCount > 0).To(x => x.HasUnannotatedFiles);
        Binder.Bind(x => x.UnannotatedTaskCount > 0).To(x => x.HasUnannotatedTasks);
        Binder.Bind(x => x.Project == null ? 0 : x.Project.TrainingBatch.MinBatchPercentage).To(x => x.BatchMinPercentage);
        Binder.Bind(x => x.Project == null ? 0 : x.Project.TrainingBatch.MaxBatchPercentage).To(x => x.BatchMaxPercentage);
        Binder.Bind(x => x.Project == null ? 0 : x.Project.TrainingBatch.BatchPercentage).To(x => x.BatchPercentage);
        Binder.Bind(x => x.Project == null ? 0 : x.Project.TrainingBatch.BatchSize).To(x => x.BatchSize);
        Binder.Bind(x => x.Project == null ? 0 : x.Project.TrainingDataset.TrainValSplitPercentage).To(x => x.TrainValSplitPercentage);
        Binder.Bind(x => GetFilteredTasks(x)).To(x => x.FilteredTasks);
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

        var predictionUiState = this.WhenAnyValue(x => x.Project)
            .Select(project => project == null
                ? Observable.Return(new PredictionUiState(null, Array.Empty<string>()))
                : Observable.CombineLatest(
                    project.Predictions.WhenAnyValue(x => x.LatestPredictions),
                    project.RemoteProject.WhenAnyValue(x => x.ProjectFiles)
                        .Select(projectFiles => projectFiles
                            .Connect()
                            .Select(_ => projectFiles.Items.Select(y => y.FileName).ToArray()))
                        .Switch(),
                    (predictions, projectFileNames) => new PredictionUiState(predictions, projectFileNames)))
            .Switch();

        Observable.CombineLatest(
                this.WhenAnyValue(x => x.PredictIncludeAnnotated),
                predictionUiState,
                (includeAnnotated, state) => new { includeAnnotated, state.Predictions, state.ProjectFileNames })
            .Throttle(UiConstants.UiThrottlingDelay)
            .Subscribe(x => RecalculatePredictItems(x.Predictions, x.includeAnnotated ? new string[0] : x.ProjectFileNames), Log.HandleUiException)
            .AddTo(Anchors);

        Binder.Attach(this).AddTo(Anchors);
    }

    public YoloEaseProject? Project { get; set; }

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

    public int ProjectFileCount { get; private set; }

    public int UnannotatedFileCount { get; private set; }

    public int AnnotatedFileCount { get; private set; }

    public int ProjectTaskCount { get; private set; }

    public int UnannotatedTaskCount { get; private set; }

    public int AnnotatedTaskCount { get; private set; }

    public bool HasUnannotatedFiles { get; private set; }

    public bool HasUnannotatedTasks { get; private set; }

    public int BatchMinPercentage { get; private set; }

    public int BatchMaxPercentage { get; private set; }

    public int BatchPercentage { get; private set; }

    public int BatchSize { get; private set; }

    public int TrainValSplitPercentage { get; private set; }

    public IReadOnlyList<AnnotationTaskInfo> FilteredTasks { get; private set; } = Array.Empty<AnnotationTaskInfo>();
    
    public float AutoAnnotateConfidenceThresholdPercentage { get; set; }

    private sealed record PredictionUiState(DatasetPredictInfo? Predictions, string[] ProjectFileNames);

    public void UpdateBatchPercentage(int value)
    {
        var project = Project;
        if (project == null)
        {
            return;
        }

        project.TrainingBatch.BatchPercentage = value;
    }

    public void UpdateTrainValSplitPercentage(int value)
    {
        var project = Project;
        if (project == null)
        {
            return;
        }

        project.TrainingDataset.TrainValSplitPercentage = value;
    }

    private static IReadOnlyList<AnnotationTaskInfo> GetFilteredTasks(AutomaticTrainer trainer)
    {
        var project = trainer.Project;
        if (project == null)
        {
            return Array.Empty<AnnotationTaskInfo>();
        }

        return (trainer.TaskFilter == AutomaticTrainerTaskFilter.All
                ? project.TrainingBatch.Tasks.Items
                : project.TrainingBatch.UnannotatedTasks.Items)
            .ToArray();
    }

    private void RecalculatePredictItems(
        DatasetPredictInfo? datasetPredict, 
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
        var project = GetRequiredProject();
        var batchEntry = new CreateTaskTimelineEntry(
            project.RemoteProject,
            project.Annotations,
            project.TrainingBatch,
            Array.Empty<FileLabel>())
        {
            Text = "Preparing next batch...",
            AutoAnnotate = false,
        }.AddTo(timelineSource);
        
        return await batchEntry.Run(CancellationToken.None);
    }

    public async Task NavigateToNextUnannotatedTask()
    {
        var project = GetRequiredProject();
        var tasks = project.TrainingBatch.UnannotatedTasks.Items.ToArray();
        if (tasks.Length <= 0)
        {
            throw new InvalidOperationException("No tasks for annotation");
        }

        var task = tasks.First();
        await project.RemoteProject.NavigateToTask(task.Id);
    }

    public Task Stop()
    {
        activeTrainingCancellationTokenSource?.Cancel();
        return Task.CompletedTask;
    }

    public async Task Start()
    {
        var project = Project;
        if (project == null)
        {
            Log.Warn("Ignoring automatic trainer start request because no project is loaded");
            return;
        }

        using var isBusy = MarkAsBusy();
        using var cleanup = Disposable.Create(() =>
        {
            activeTrainingCancellationTokenSource?.Dispose();
            activeTrainingCancellationTokenSource = null;
        });
        activeTrainingCancellationTokenSource = new CancellationTokenSource();
        var cancellationTokenSource = activeTrainingCancellationTokenSource;

        timelineSource.Add(new BasicTimelineEntry
        {
            Text = "Automatic training started",
            Timestamp = clock.Now
        });

        await Task.Run(() => HandleTraining(project, cancellationTokenSource.Token));

        timelineSource.Add(new BasicTimelineEntry
        {
            Text = "Automatic training stopped",
            Timestamp = clock.Now
        });
    }
    
    protected override async Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
        var project = Project;
        if (project == null)
        {
            Log.Debug("Ignoring trainer refresh because no project is loaded");
            return;
        }

        await project.Refresh(progressReporter);
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

    private async Task HandleTraining(YoloEaseProject project, CancellationToken cancellationToken)
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

                var projectEntry = new ProjectTimelineEntry(project).AddTo(timelineSource);
                await projectEntry.Run(cancellationToken);

                if (project.Predictions.PredictionModel == null && ModelStrategy == AutomaticTrainerModelStrategy.Latest)
                {
                    if (project.TrainingDataset.TrainedModels.Count <= 0)
                    {
                        new BasicTimelineEntry()
                        {
                            Text = $"Model is not set, refreshing trained models list",
                            Timestamp = clock.Now
                        }.AddTo(timelineSource);
                        await project.TrainingDataset.Refresh();
                    }
                    
                    if (project.TrainingDataset.TrainedModels.Count > 0)
                    {
                        var modelToPick = project.TrainingDataset.TrainedModels.Items
                            .OrderByDescending(x => x.ModelFile.LastWriteTime)
                            .First();
                        project.Predictions.PredictionModel = modelToPick;
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
                
                await project.TrainingDataset.Refresh();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                
                var trainingSettings = project.TrainingDataset.TrainingSettings;
                if (lastTrainedDataset is {ProjectInfo: not null})
                {
                    var changesetEntry = new ChangesetTimelineEntry(
                        lastTrainedDataset,
                        lastTrainingSettings,
                        trainingSettings,
                        project.Annotations, project.RemoteProject).AddTo(timelineSource);
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

                var createDatasetEntry = new CreateDatasetTimelineEntry(project.Annotations, project.Augmentations).AddTo(timelineSource);
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
                    
                    var preTrainingProgressEntry = preTrainingEntryFactory.Create(timelineController, datasetInfo, project.TrainingDataset).AddTo(timelineSource);
                    await preTrainingProgressEntry.Run(cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }


                    try
                    {
                        var trainingProgressEntry = trainingEntryFactory.Create(timelineController, datasetInfo, project.TrainingDataset, project.Predictions).AddTo(timelineSource);
                        var modelFile = await trainingProgressEntry.Run(cancellationToken);
                        
                        if (ModelStrategy == AutomaticTrainerModelStrategy.Latest)
                        {
                            project.Predictions.PredictionModel = modelFile;
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

    private async Task AddPredictionsIfNeeded(YoloEaseProject project, CancellationToken cancellationToken)
    {
        var predictions = project.Predictions;
        var predictionModel = predictions.PredictionModel;
        if (predictionModel == null)
        {
            return;
        }

        var directories = project.Assets.InputDirectories.Items.ToArray();
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
                filesToRunPredictOn = project.TrainingBatch.UnannotatedFiles.Items.ToArray().PickPercentage(PredictBatchPercentage);;
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
            project.TrainingDataset, 
            project.Predictions).AddTo(timelineSource);
        var actualPredictions = await predictEntry.Run(cancellationToken);
        project.Predictions.LatestPredictions = actualPredictions;
    }

    private YoloEaseProject GetRequiredProject()
    {
        return Project ?? throw new InvalidOperationException("No project is loaded");
    }
}
