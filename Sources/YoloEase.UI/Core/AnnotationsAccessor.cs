using System.Collections.Concurrent;
using System.Linq;
using ByteSizeLib;
using PoeShared.Logging;
using YoloEase.UI.Dto;
using YoloEase.UI.Yolo;

namespace YoloEase.UI.Core;

/// <summary>
/// Loads annotation XML files for the current project and exposes task-level annotation metadata.
/// </summary>
public class AnnotationsAccessor : RefreshableReactiveObject
{
    private readonly AnnotationsCache annotationsCache;
    private readonly IConfigSerializer configSerializer;
    private readonly SourceCache<AnnotationTaskInfo, int> annotatedTaskSource = new(x => x.Id);
    private readonly SourceCache<TaskAnnotationFileInfo, int> annotationsSource = new(x => x.TaskId);

    public AnnotationsAccessor(
        AnnotationProjectAccessor remoteProject,
        Yolo8DatasetAccessor training,
        AnnotationsCache annotationsCache,
        IConfigSerializer configSerializer,
        IFileAssetsAccessor assets)
    {
        RemoteProject = remoteProject;
        Training = training;
        Assets = assets;
        this.annotationsCache = annotationsCache;
        this.configSerializer = configSerializer;

        AnnotatedTasks = annotatedTaskSource.Connect().RemoveKey().ToSourceListEx().AddTo(Anchors);
        Annotations = annotationsSource.Connect().RemoveKey().ToSourceListEx().AddTo(Anchors);
    }

    public IObservableListEx<TaskAnnotationFileInfo> Annotations { get; }

    public IObservableListEx<AnnotationTaskInfo> AnnotatedTasks { get; }

    public AnnotationProjectAccessor RemoteProject { get; }

    public Yolo8DatasetAccessor Training { get; }

    public IFileAssetsAccessor Assets { get; }

    public async Task RemoveAnnotationsFile(TaskAnnotationFileInfo annotationFileInfo)
    {
        if (!string.IsNullOrEmpty(annotationFileInfo.FilePath))
        {
            Log.Info($"Removing linked annotations file: {annotationFileInfo}");
            File.Delete(annotationFileInfo.FilePath);
        }
        
        Log.Info($"Removing annotation record from the list: {annotationFileInfo}");
        annotationsSource.Remove(annotationFileInfo);
    }

    protected override async Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
        var annotatedTasks = RemoteProject.Tasks.Items.Where(x => x.Status == AnnotationTaskStatus.Completed).ToArray();
        annotatedTaskSource.EditDiff(annotatedTasks);

        await RefreshAnnotations(downloadIfMissing: false);
    }

    public Task DownloadAnnotations()
    {
        return RefreshAnnotations(downloadIfMissing: true);
    }

    public Task<AnnotationUpdateResult> UploadAnnotations(int taskId, CvatRectangleAnnotation[] labels)
    {
        return RemoteProject.UploadAnnotations(taskId, labels);
    }

    public async Task RefreshAnnotations(bool downloadIfMissing)
    {
        var tasks = annotatedTaskSource.Items.ToArray();
        var localFiles = Assets.Files.Items.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
        if (tasks.Length > 0 && localFiles.Count <= 0)
        {
            Log.Warn($"Skipping annotation file binding for {tasks.Length} completed task(s) because the project has no local files. Data sources may be missing or still refreshing.");
            annotationsSource.EditDiff(tasks.Select(CreateEmpty).ToArray());
            return;
        }

        var projectId = RemoteProject.ProjectId;
        var annotations = new ConcurrentBag<TaskAnnotationFileInfo>();
        await Parallel.ForEachAsync(tasks, new ParallelOptions()
        {
            MaxDegreeOfParallelism = 16
        }, async (task, token) =>
        {
            using var log = new BenchmarkTimer("Download annotations", Log.WithPrefix($"Task {task.Id} {task.Name}"))
                .WithLoggingOnDisposal();
            log.Step("Analyzing the task");

            log.Step("Extracting task files");
            var taskFiles = RemoteProject.ProjectFiles.Items.Where(x => x.TaskId == task.Id).ToArray();

            if (!taskFiles.Any())
            {
                log.Step("Task does contain any associated files with it");
                return;
            }

            log.Step($"Found {taskFiles.Length} attached to the task");

            var filesMapping = taskFiles.Select(taskFile => new
            {
                TaskFile = taskFile.FileName,
                LocalFile = localFiles.GetOrDefault(taskFile.FileName)
            }).ToArray();

            var missingFiles = filesMapping.Where(x => x.LocalFile == null).ToArray();
            if (missingFiles.Any())
            {
                log.Step($"Missing files count: {missingFiles.Length}, first 10: {missingFiles.Select(x => x.TaskFile).Take(10).DumpToString()}");
            }

            var directories = filesMapping
                .Where(x => x.LocalFile != null)
                .Select(x => x.LocalFile!.Directory)
                .Where(x => x != null)
                .Select(x => x!)
                .DistinctBy(x => x.FullName)
                .ToArray();

            if (directories.Length > 1)
            {
                //FIXME Probably should add support for shared files
                log.Step($"Multiple owning directories detected: {directories.Select(x => x.FullName).DumpToString()}");
                annotations.Add(CreateEmpty(task));
                return;
            }

            if (directories.Length <= 0)
            {
                log.Step($"Task {task} is not linked to any input directory");
                annotations.Add(CreateEmpty(task)); //create empty task which will be showing that we've done everything we could
                return;
            }

            var dataDirectory = directories.Single();
            var outputFile = new FileInfo(Path.Combine(dataDirectory.FullName, $"annotations.project.{projectId}.task.{task.Id}.xml"));

            if (!outputFile.Exists && downloadIfMissing)
            {
                log.Step($"Downloading annotations file to {outputFile.FullName}");
                await RemoteProject.ExportAnnotations(task.Id, outputFile);
                log.Step($"Downloaded annotations file to {outputFile.FullName}");
            }

            if (!outputFile.Exists)
            {
                log.Step($"Annotations file not found: {outputFile.FullName}");
                return;
            }

            log.Step($"Annotations file is ready: {outputFile.FullName} ({ByteSize.FromBytes(outputFile.Length)})");

            annotations.Add(new TaskAnnotationFileInfo()
            {
                TaskId = task.Id,
                FilePath = outputFile.FullName,
                FileSize = outputFile.Length,
                TaskName = task.Name
            });
        });

        annotationsSource.EditDiff(annotations);
    }

    public async Task<DatasetInfo> CreateAnnotatedDataset(
        IReadOnlyList<FileInfo> annotationFiles,
        Action<YoloCommandOutput>? outputHandler = null)
    {
        var annotations = Annotations.Items.ToArray();
        var datasetInfo = await Training.CreateAnnotatedDataset(annotationFiles, RemoteProject, outputHandler);
        var annotatedTasksById = RemoteProject.Tasks.Items.ToDictionary(x => x.Id);
        return datasetInfo with
        {
            ProjectInfo = datasetInfo.ProjectInfo with
            {
                Tasks = annotations.Select(x => x.TaskId).ToArray(),
                TaskRevisions = annotations
                    .Select(x => annotatedTasksById.GetValueOrDefault(x.TaskId))
                    .Where(x => x != null)
                    .Select(x => x!)
                    .Select(x => new TaskRevisionInfo
                    {
                        TaskId = x.Id,
                        Revision = x.Revision,
                    })
                    .ToArray(),
            }
        };
    }
    
    private static TaskAnnotationFileInfo CreateEmpty(AnnotationTaskInfo task)
    {
        return new TaskAnnotationFileInfo()
        {
            TaskId = task.Id,
            TaskName = task.Name
        };
    }
}
