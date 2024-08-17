using System.Collections.Concurrent;
using System.Linq;
using ByteSizeLib;
using CvatApi;
using PoeShared.Logging;
using YoloEase.UI.Dto;

namespace YoloEase.UI.Core;

public class AnnotationsAccessor : RefreshableReactiveObject
{
    private readonly AnnotationsCache annotationsCache;
    private readonly IConfigSerializer configSerializer;
    private readonly SourceCache<TaskRead, int> annotatedTaskSource = new(x => x.Id.Value);
    private readonly SourceCache<TaskAnnotationFileInfo, int> annotationsSource = new(x => x.TaskId);

    public AnnotationsAccessor(
        CvatProjectAccessor remoteProject,
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

    public IObservableListEx<TaskRead> AnnotatedTasks { get; }

    public CvatProjectAccessor RemoteProject { get; }

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

    public async Task Refresh()
    {
        if (isBusyLatch.IsBusy)
        {
            throw new InvalidOperationException("Another refresh is already in progress");
        }
        var annotatedTasks = RemoteProject.Tasks.Items.Where(x => x.Status == JobStatus.Completed).ToArray();
        annotatedTaskSource.EditDiff(annotatedTasks);

        await RefreshAnnotations(downloadIfMissing: false);
    }

    public Task DownloadAnnotations()
    {
        return RefreshAnnotations(downloadIfMissing: true);
    }

    public async Task<AnnotationsRead> UploadAnnotations(int taskId, CvatRectangleAnnotation[] labels)
    {
        return await RemoteProject.Client.Api.RunAuthenticated(async httpClient =>
        {
            var annotationsClient = new CvatTasksClient(httpClient);

            var body = new PatchedLabeledDataRequest()
            {
                Shapes = new List<LabeledShapeRequest>(),
            };

            foreach (var label in labels)
            {
                var yoloRect = label.BoundingBox;
                body.Shapes.Add(new LabeledShapeRequest()
                {
                    Label_id = label.LabelId,
                    Type = ShapeType.Rectangle,
                    Frame = label.FrameIndex,
                    Points = new List<double>()
                    {
                        yoloRect.X,
                        yoloRect.Y,
                        yoloRect.X + yoloRect.Width,
                        yoloRect.Y + yoloRect.Height
                    },
                    Source = "automatic"
                });
            }

            var updatedAnnotations = await annotationsClient.Tasks_partial_update_annotationsAsync(id: taskId, action: Action9.Create, body: body);
            return updatedAnnotations;
        });
    }

    public async Task RefreshAnnotations(bool downloadIfMissing)
    {
        var tasks = annotatedTaskSource.Items.ToArray();
        var localFiles = Assets.Files.Items.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
        if (tasks.Length > 0 && localFiles.Count <= 0)
        {
            throw new InvalidOperationException("Local files list is empty");
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
                .Select(x => x.LocalFile.Directory)
                .Where(x => x != null)
                .DistinctBy(x => x.FullName)
                .ToArray();

            if (directories.Length > 1)
            {
                //FIXME Probably should add support for shared files
                log.Step($"Multiple owning directories detected: {directories.Select(x => x.FullName).DumpToString()}");
                throw new ArgumentException($"Multiple root directories detected: {directories.Select(x => x.FullName).DumpToString()}");
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
                await RemoteProject.Client.Cli.DownloadAnnotations(
                    task.Id.Value,
                    outputFile: outputFile);
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
                TaskId = task.Id.Value,
                FilePath = outputFile.FullName,
                FileSize = outputFile.Length,
                TaskName = task.Name ?? $"Task #{task.Id}"
            });
        });

        annotationsSource.EditDiff(annotations);
    }

    public async Task<DatasetInfo> CreateAnnotatedDataset(IReadOnlyList<FileInfo> annotationFiles)
    {
        var annotations = Annotations.Items.ToArray();
        var datasetInfo = await Training.CreateAnnotatedDataset(annotationFiles, RemoteProject);
        return datasetInfo with
        {
            ProjectInfo = datasetInfo.ProjectInfo with
            {
                Tasks = annotations.Select(x => x.TaskId).ToArray(),
            }
        };
    }
    
    private static TaskAnnotationFileInfo CreateEmpty(TaskRead task)
    {
        if (task.Id == null)
        {
            throw new ArgumentException($"Task Id must be set but was not for {task}");
        }
        return new TaskAnnotationFileInfo()
        {
            TaskId = task.Id.Value,
            TaskName = task.Name ?? $"Task #{task.Id}"
        };
    }
}