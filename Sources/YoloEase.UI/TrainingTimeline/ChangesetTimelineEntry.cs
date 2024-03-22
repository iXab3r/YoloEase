using System.Linq;
using System.Threading;
using CvatApi;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TrainingTimeline;

public class ChangesetTimelineEntry : RunnableTimelineEntry<IReadOnlyList<Changeset>>
{
    private readonly DatasetInfo lastDatasetInfo;
    private readonly ModelTrainingSettings lastModelTrainingSettings;
    private readonly ModelTrainingSettings actualModelTrainingSettings;
    private readonly AnnotationsAccessor annotationsAccessor;
    private readonly CvatProjectAccessor projectAccessor;
    private readonly SourceListEx<int> newTasksSource = new();

    public ChangesetTimelineEntry(
        DatasetInfo lastDatasetInfo,
        ModelTrainingSettings lastModelTrainingSettings,
        ModelTrainingSettings actualModelTrainingSettings,
        AnnotationsAccessor annotationsAccessor, 
        CvatProjectAccessor projectAccessor)
    {
        this.lastDatasetInfo = lastDatasetInfo;
        this.lastModelTrainingSettings = lastModelTrainingSettings;
        this.actualModelTrainingSettings = actualModelTrainingSettings;
        this.annotationsAccessor = annotationsAccessor;
        this.projectAccessor = projectAccessor;
    }

    public IObservableListEx<int> NewTasks => newTasksSource;

    protected override async Task<IReadOnlyList<Changeset>> RunInternal(CancellationToken cancellationToken)
    {
        var lastTrainedProject = lastDatasetInfo?.ProjectInfo;
        if (lastTrainedProject == null)
        {
            return Array.Empty<Changeset>();
        }

        var trainingChangeset = GetChangeset(lastModelTrainingSettings, actualModelTrainingSettings);
        var projectChangeset = GetChangeset(
            lastTrainedProject.Tasks, 
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
            newTasksSource.EditDiff(projectChangeset.NewAnnotatedTasks);
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
        IEnumerable<int> trainedTasks, 
        IEnumerable<TaskRead> annotatedTasks, 
        IEnumerable<TaskFileInfo> projectFiles)
    {
        var annotatedTasksById = annotatedTasks
            .ToDictionary(x => x.Id, x => x);
        var annotatedFiles = projectFiles
            .Where(x => x.TaskId != null)
            .Where(x => annotatedTasksById.ContainsKey(x.TaskId.Value))
            .ToArray();

        var remoteSnapshot = new YoloEaseProjectInfo
        {
            Files = annotatedFiles.Select(x => x.FileName).ToArray(),
            Tasks = annotatedTasksById.Keys.Select(x => x.Value).ToArray()
        };
        var newTasks = remoteSnapshot.Tasks.Except(trainedTasks).ToList();
        if (newTasks.IsEmpty())
        {
            return ProjectChangeset.Empty;
        }

        return new ProjectChangeset()
        {
            NewAnnotatedTasks = newTasks
        };
    }
}