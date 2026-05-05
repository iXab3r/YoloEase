using System.Linq;
using System.Threading;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TrainingTimeline;

/// <summary>
/// Timeline step that detects project changes before later training operations run.
/// </summary>
public class ChangesetTimelineEntry : RunnableTimelineEntry<IReadOnlyList<Changeset>>
{
    private readonly DatasetInfo lastDatasetInfo;
    private readonly ModelTrainingSettings lastModelTrainingSettings;
    private readonly ModelTrainingSettings actualModelTrainingSettings;
    private readonly AnnotationsAccessor annotationsAccessor;
    private readonly AnnotationProjectAccessor projectAccessor;
    private readonly SourceListEx<int> newTasksSource = new();

    public ChangesetTimelineEntry(
        DatasetInfo lastDatasetInfo,
        ModelTrainingSettings lastModelTrainingSettings,
        ModelTrainingSettings actualModelTrainingSettings,
        AnnotationsAccessor annotationsAccessor, 
        AnnotationProjectAccessor projectAccessor)
    {
        this.lastDatasetInfo = lastDatasetInfo;
        this.lastModelTrainingSettings = lastModelTrainingSettings;
        this.actualModelTrainingSettings = actualModelTrainingSettings;
        this.annotationsAccessor = annotationsAccessor;
        this.projectAccessor = projectAccessor;
    }

    public IObservableList<int> NewTasks => newTasksSource;

    protected override async Task<IReadOnlyList<Changeset>> RunInternal(CancellationToken cancellationToken)
    {
        var lastTrainedProject = lastDatasetInfo?.ProjectInfo;
        if (lastTrainedProject == null)
        {
            return Array.Empty<Changeset>();
        }

        var trainingChangeset = GetChangeset(lastModelTrainingSettings, actualModelTrainingSettings);
        var projectChangeset = GetChangeset(
            lastTrainedProject.TaskRevisions.EmptyIfNull(), 
            annotationsAccessor.AnnotatedTasks.Items, 
            projectAccessor.ProjectFiles.Items);

        var allChangesets = new Changeset[] { trainingChangeset, projectChangeset };

        var changes = new List<string>();

        if (!trainingChangeset.IsEmpty)
        {
            changes.Add($"Training settings have changed, actual: {trainingChangeset.Current}");
        }

        if (!projectChangeset.IsEmpty)
        {
            changes.Add($"Annotated tasks have changed");
            newTasksSource.EditDiff(projectChangeset.ChangedAnnotatedTasks);
        }

        Text = changes.Any() ? string.Join(", ", changes) : "No changes detected";
        return allChangesets.Where(x => !x.IsEmpty).ToArray();
    }

    private static TrainingSettingsChangeset GetChangeset(
        ModelTrainingSettings lastTrainingSettings,
        ModelTrainingSettings actualTrainingSettings)
    {
        if (lastTrainingSettings == actualTrainingSettings)
        {
            return TrainingSettingsChangeset.Empty;
        }

        return new TrainingSettingsChangeset()
        {
            Previous = lastTrainingSettings,
            Current = actualTrainingSettings
        };
    }
    
    private static ProjectChangeset GetChangeset(
        IEnumerable<TaskRevisionInfo> trainedTasks, 
        IEnumerable<AnnotationTaskInfo> annotatedTasks, 
        IEnumerable<TaskFileInfo> projectFiles)
    {
        var annotatedTasksById = annotatedTasks.ToDictionary(x => x.Id, x => x);
        var annotatedFiles = projectFiles
            .Where(x => x.TaskId != null)
            .Where(x => x.TaskId is { } taskId && annotatedTasksById.ContainsKey(taskId))
            .ToArray();

        var trainedTaskRevisions = trainedTasks.ToDictionary(x => x.TaskId, x => x.Revision);
        var remoteSnapshot = new YoloEaseProjectInfo
        {
            Files = annotatedFiles.Select(x => x.FileName).ToArray(),
            Tasks = annotatedTasksById.Keys.ToArray(),
            TaskRevisions = annotatedTasksById.Values.Select(x => new TaskRevisionInfo
            {
                TaskId = x.Id,
                Revision = x.Revision,
            }).ToArray(),
        };
        var changedTasks = remoteSnapshot.TaskRevisions
            .Where(x => !trainedTaskRevisions.TryGetValue(x.TaskId, out var revision) || revision != x.Revision)
            .Select(x => x.TaskId)
            .ToList();
        if (changedTasks.IsEmpty())
        {
            return ProjectChangeset.Empty;
        }

        return new ProjectChangeset()
        {
            ChangedAnnotatedTasks = changedTasks,
        };
    }
}
