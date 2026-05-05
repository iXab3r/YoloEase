using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AntDesign;
using CvatApi;
using PoeShared.Services;
using YoloEase.UI.Dto;
using YoloEase.UI.Scaffolding;

namespace YoloEase.UI.Core;

/// <summary>
/// Coordinates CVAT and offline annotation projects, task metadata, labels, frames, and annotation XML storage.
/// </summary>
public class AnnotationProjectAccessor : RefreshableReactiveObject
{
    private static readonly Binder<AnnotationProjectAccessor> Binder = new();
    private static readonly Regex OfflineAnnotationFileNameRegex = new(
        @"annotations\.project\.(?<projectId>\d+)\.task\.(?<taskId>\d+)\.xml$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ICvatClient cvatClient;
    private readonly IUniqueIdGenerator idGenerator;
    private readonly IConfigSerializer configSerializer;
    private readonly SourceCacheEx<TaskFileInfo, string> projectFileSource = new(GetTaskFileCacheKey);
    private readonly SourceCacheEx<AnnotationTaskInfo, int> taskSource = new(x => x.Id);
    private readonly SourceCacheEx<AnnotationProjectInfoItem, int> projectsSources = new(x => x.Id);
    private readonly SourceCacheEx<AnnotationLabelInfo, int> labelSource = new(x => x.Id);
    private readonly SourceCacheEx<AnnotationJobInfo, int> jobsSource = new(x => x.Id);

    static AnnotationProjectAccessor()
    {
        Binder.Bind(x => x.Username).To(x => x.cvatClient.Username);
        Binder.Bind(x => x.Password).To(x => x.cvatClient.Password);
        Binder.Bind(x => x.ServerUrl).To(x => x.cvatClient.ServerUrl);
        Binder.Bind(x => x.ProjectId != 0 && !string.IsNullOrEmpty(x.ProjectName)).To(x => x.IsReady);
    }

    public AnnotationProjectAccessor(
        ICvatClient cvatClient,
        IUniqueIdGenerator idGenerator,
        IConfigSerializer configSerializer)
    {
        this.cvatClient = cvatClient;
        this.idGenerator = idGenerator;
        this.configSerializer = configSerializer;
        Binder.Attach(this).AddTo(Anchors);
    }

    public DirectoryInfo? StorageDirectory { get; set; }

    public FileInfo? ProjectFile { get; private set; }

    public AnnotationBackendMode Mode { get; set; } = AnnotationBackendMode.Offline;

    public string Username { get; set; } = Environment.UserName;

    public string Password { get; set; } = string.Empty;

    public string ServerUrl { get; set; } = "https://cvat.eyeauras.net";

    public int ProjectId { get; set; }

    public int? OrganizationId { get; private set; }

    public string? OrganizationName { get; private set; }

    public string ProjectName { get; set; } = "Offline Project";

    public bool IsReady { get; private set; }

    public IObservableCacheEx<AnnotationTaskInfo, int> Tasks => taskSource;

    public IObservableCacheEx<TaskFileInfo, string> ProjectFiles => projectFileSource;

    public IObservableCacheEx<AnnotationLabelInfo, int> Labels => labelSource;

    public IObservableCacheEx<AnnotationProjectInfoItem, int> Projects => projectsSources;

    public IObservableCacheEx<AnnotationJobInfo, int> Jobs => jobsSource;

    public AnnotationUserInfo? CurrentUser { get; private set; }

    public int? ActiveTaskId { get; private set; }

    public void SetProjectFile(FileInfo? projectFile)
    {
        ProjectFile = projectFile;
        if (Mode == AnnotationBackendMode.Offline)
        {
            ProjectName = ResolveOfflineProjectName();
        }
    }

    public async Task Logout()
    {
        if (Mode == AnnotationBackendMode.Offline)
        {
            CurrentUser = null;
            return;
        }

        await cvatClient.Api.Logout();
        CurrentUser = null;
        ClearCachedState();
    }

    public async Task Login()
    {
        if (Mode == AnnotationBackendMode.Offline)
        {
            CurrentUser = new AnnotationUserInfo
            {
                Username = string.IsNullOrWhiteSpace(Username) ? Environment.UserName : Username,
            };
            EnsureOfflineProjectIdentity();
            await Refresh();
            return;
        }

        var user = await cvatClient.Api.Login();
        CurrentUser = new AnnotationUserInfo
        {
            Username = string.IsNullOrWhiteSpace(Username) ? Environment.UserName : Username,
        };
    }

    public async Task ApplyMode(AnnotationBackendMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        ActiveTaskId = null;
        CurrentUser = null;
        ClearCachedState();

        if (mode == AnnotationBackendMode.Offline)
        {
            ProjectName = ResolveOfflineProjectName();
            await Login();
            await Refresh();
            return;
        }

        if (!string.IsNullOrWhiteSpace(Username) &&
            !string.IsNullOrWhiteSpace(Password) &&
            !string.IsNullOrWhiteSpace(ServerUrl))
        {
            await Login();
            await Refresh();
        }
    }

    public async Task DeleteTask(int taskId)
    {
        if (Mode == AnnotationBackendMode.Offline)
        {
            var taskDirectory = GetOfflineTaskDirectory(taskId);
            if (taskDirectory.Exists)
            {
                taskDirectory.Delete(recursive: true);
            }

            if (ActiveTaskId == taskId)
            {
                ActiveTaskId = null;
            }

            await Refresh();
            return;
        }

        await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var taskClient = new CvatTasksClient(httpClient);
            await taskClient.Tasks_destroyAsync(id: taskId);
            taskSource.RemoveKey(taskId);
        });
    }

    public async Task NavigateToTask(int taskId)
    {
        ActiveTaskId = taskId;
        if (Mode == AnnotationBackendMode.Offline)
        {
            return;
        }

        var job = jobsSource.Items.FirstOrDefault(x => x.TaskId == taskId);
        var relativePath = job == null ? $"tasks/{taskId}" : $"tasks/{taskId}/jobs/{job.Id}";
        await ProcessUtils.OpenUri($"{ResolveServerUrl()}/{relativePath}");
    }

    public string ResolveProjectUrl(int projectId)
    {
        if (Mode == AnnotationBackendMode.Offline)
        {
            return $"offline://project/{projectId}";
        }

        return $"{ResolveServerUrl()}/projects/{projectId}";
    }

    public async Task NavigateToProject(int projectId)
    {
        if (Mode == AnnotationBackendMode.Offline)
        {
            var directory = StorageDirectory;
            if (directory == null)
            {
                throw new InvalidOperationException("Storage directory is not configured");
            }

            if (!directory.Exists)
            {
                directory.Create();
            }
            await ProcessUtils.OpenFolder(directory);
            return;
        }

        await ProcessUtils.OpenUri(ResolveProjectUrl(projectId));
    }

    public async Task<AnnotationDataMeta> RetrieveMetadata(int taskId)
    {
        if (Mode == AnnotationBackendMode.Offline)
        {
            return await RetrieveOfflineMetadata(taskId);
        }

        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var taskClient = new CvatTasksClient(httpClient);
            var metadata = await taskClient.Tasks_retrieve_data_metaAsync(taskId);
            if (metadata?.Frames == null)
            {
                throw new InvalidOperationException($"Failed to get metadata of task {taskId}");
            }

            return new AnnotationDataMeta
            {
                Frames = metadata.Frames
                    .Select((frame, frameIdx) => new AnnotationFrameInfo
                    {
                        Index = frameIdx,
                        Name = frame.Name ?? string.Empty,
                        Width = frame.Width,
                        Height = frame.Height,
                    })
                    .ToArray(),
            };
        });
    }

    public FileInfo? ResolveTaskFrameFile(string fileName)
    {
        if (Mode != AnnotationBackendMode.Offline || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return ResolveOfflineTaskFile(fileName);
    }

    public async Task<AnnotationTaskInfo> CreateTask(IReadOnlyList<FileInfo> filesToAdd)
    {
        if (filesToAdd.Count <= 0)
        {
            throw new ArgumentException("There must be at least one file in the next batch");
        }

        if (Mode == AnnotationBackendMode.Offline)
        {
            return await CreateOfflineTask(filesToAdd);
        }

        var taskId = await cvatClient.Cli.CreateTask(
            projectId: ProjectId,
            organization: OrganizationName ?? string.Empty,
            taskName: $"Task {idGenerator.Next()}",
            filesToUpload: filesToAdd);

        var task = await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var taskClient = new CvatTasksClient(httpClient);
            var taskRead = await taskClient.Tasks_retrieveAsync(taskId);
            if (taskRead == null)
            {
                throw new InvalidOperationException($"Failed to retrieve task {taskId}");
            }

            return MapTask(taskRead);
        });

        taskSource.AddOrUpdate(task);
        return task;
    }

    public async Task<AnnotationUpdateResult> UploadAnnotations(int taskId, CvatRectangleAnnotation[] labels)
    {
        if (Mode == AnnotationBackendMode.Offline)
        {
            await SaveOfflineTaskAnnotations(taskId, labels, labels.Any() ? AnnotationTaskStatus.InProgress : AnnotationTaskStatus.New);
            return new AnnotationUpdateResult
            {
                ShapesCount = labels.Length,
            };
        }

        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var annotationsClient = new CvatTasksClient(httpClient);
            var body = new PatchedLabeledDataRequest
            {
                Shapes = new List<LabeledShapeRequest>(),
            };

            foreach (var label in labels)
            {
                if (label.Kind != CvatAnnotationShapeKind.Rectangle)
                {
                    throw new NotSupportedException($"Annotation shape kind '{label.Kind}' is not supported yet");
                }

                var yoloRect = label.BoundingBox;
                body.Shapes.Add(new LabeledShapeRequest
                {
                    Label_id = label.LabelId,
                    Type = ShapeType.Rectangle,
                    Frame = label.FrameIndex,
                    Rotation = NormalizeRotation(label.RotationDegrees),
                    Points = new List<double>
                    {
                        yoloRect.X,
                        yoloRect.Y,
                        yoloRect.X + yoloRect.Width,
                        yoloRect.Y + yoloRect.Height,
                    },
                    Source = string.IsNullOrWhiteSpace(label.Source) ? "automatic" : label.Source,
                });
            }

            var updatedAnnotations = await annotationsClient.Tasks_partial_update_annotationsAsync(id: taskId, action: Action9.Create, body: body);
            var shapesCount = updatedAnnotations?.Shapes?.Count ?? labels.Length;
            return new AnnotationUpdateResult
            {
                ShapesCount = shapesCount,
            };
        });
    }

    public async Task ExportAnnotations(int taskId, FileInfo outputFile)
    {
        if (Mode == AnnotationBackendMode.Offline)
        {
            await ExportOfflineAnnotations(taskId, outputFile);
            return;
        }

        await cvatClient.Cli.DownloadAnnotations(taskId, outputFile);
    }

    public async Task<IReadOnlyList<CvatRectangleAnnotation>> RetrieveTaskAnnotations(int taskId)
    {
        if (Mode != AnnotationBackendMode.Offline)
        {
            return Array.Empty<CvatRectangleAnnotation>();
        }

        var annotationsXmlFile = GetOfflineTaskAnnotationsXmlFile(taskId);
        if (annotationsXmlFile.Exists)
        {
            return await ReadOfflineTaskAnnotationsXml(taskId, annotationsXmlFile);
        }

        var annotationsJsonFile = GetOfflineTaskAnnotationsJsonFile(taskId);
        if (!annotationsJsonFile.Exists)
        {
            return Array.Empty<CvatRectangleAnnotation>();
        }

        var content = await File.ReadAllTextAsync(annotationsJsonFile.FullName);
        var state = configSerializer.Deserialize<OfflineTaskAnnotationsState>(content);
        return state.Shapes.EmptyIfNull().ToArray();
    }

    public async Task SaveTaskAnnotations(int taskId, IReadOnlyList<CvatRectangleAnnotation> labels, AnnotationTaskStatus status)
    {
        if (Mode != AnnotationBackendMode.Offline)
        {
            throw new NotSupportedException("Manual task editing is available only for offline projects");
        }

        await SaveOfflineTaskAnnotations(taskId, labels, status);
        await Refresh();
    }

    public async Task RemoveTaskFrame(int taskId, int frameIndex, bool deleteImageFile)
    {
        if (Mode != AnnotationBackendMode.Offline)
        {
            throw new NotSupportedException("Removing images from a task is available only for offline projects");
        }

        var task = await ReadOfflineTask(taskId);
        if (task == null)
        {
            throw new InvalidOperationException($"Offline task {taskId} was not found");
        }

        if (frameIndex < 0 || frameIndex >= task.Files.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex), $"Frame {frameIndex} is outside task {taskId}");
        }

        var removedFileName = task.Files[frameIndex];
        var annotations = (await RetrieveTaskAnnotations(taskId))
            .Where(x => x.FrameIndex != frameIndex)
            .Select(x => x.FrameIndex > frameIndex ? x with { FrameIndex = x.FrameIndex - 1 } : x)
            .ToArray();

        task.Files = task.Files
            .Where((_, index) => index != frameIndex)
            .ToArray();
        task.Revision++;
        task.UpdatedAt = DateTimeOffset.UtcNow;

        if (task.Files.Length <= 0)
        {
            task.Status = AnnotationTaskStatus.New;
            task.CompletedAt = null;
        }

        await SaveOfflineTask(task);
        UpdateOfflineTaskCache(task);
        await WriteOfflineTaskAnnotationsXml(task, annotations);

        if (deleteImageFile)
        {
            var imageFile = ResolveOfflineTaskFile(removedFileName);
            var isUsedByAnotherTask = (await ReadOfflineTasks(ProjectId))
                .Where(x => x.TaskId != taskId)
                .SelectMany(x => x.Files.EmptyIfNull())
                .Any(x => x.Equals(removedFileName, StringComparison.OrdinalIgnoreCase));
            if (!isUsedByAnotherTask && imageFile.Exists)
            {
                imageFile.Delete();
            }
        }

        await Refresh();
    }

    public async Task AddLabel(string labelName, string? color = null)
    {
        if (Mode != AnnotationBackendMode.Offline)
        {
            throw new NotSupportedException("Labels can be edited only for offline projects");
        }

        labelName = labelName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(labelName))
        {
            throw new ArgumentException("Label name must be specified");
        }

        var state = ResolveOfflineProjectIdentity();
        var labels = await ReadOfflineLabels(state.ProjectId);
        if (labels.Any(x => x.Name.Equals(labelName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Label '{labelName}' already exists");
        }

        var nextLabelId = GetNextOfflineLabelId(labels);
        var label = new OfflineLabelState
        {
            Id = nextLabelId,
            Name = labelName,
            Color = NormalizeLabelColor(color, nextLabelId),
        };

        labels.Add(label);
        await SaveOfflineLabels(state.ProjectId, labels);
        await Refresh();
    }

    public async Task DeleteLabel(int labelId)
    {
        if (Mode != AnnotationBackendMode.Offline)
        {
            throw new NotSupportedException("Labels can be edited only for offline projects");
        }

        var state = ResolveOfflineProjectIdentity();
        var tasks = await ReadOfflineTasks(state.ProjectId);
        foreach (var task in tasks)
        {
            var annotations = await RetrieveTaskAnnotations(task.TaskId);
            if (annotations.Any(x => x.LabelId == labelId))
            {
                throw new InvalidOperationException("Cannot delete a label that is already used in task annotations");
            }
        }

        var labels = await ReadOfflineLabels(state.ProjectId);
        labels.RemoveAll(x => x.Id == labelId);
        await SaveOfflineLabels(state.ProjectId, labels);
        await Refresh();
    }

    public async Task UpdateLabelColor(int labelId, string? color)
    {
        if (Mode != AnnotationBackendMode.Offline)
        {
            throw new NotSupportedException("Labels can be edited only for offline projects");
        }

        var state = ResolveOfflineProjectIdentity();
        var labels = await ReadOfflineLabels(state.ProjectId);
        var label = labels.FirstOrDefault(x => x.Id == labelId);
        if (label == null)
        {
            throw new InvalidOperationException($"Label #{labelId} was not found");
        }

        var updatedLabel = label with
        {
            Color = NormalizeLabelColor(color, labelId),
        };

        labels[labels.IndexOf(label)] = updatedLabel;
        await SaveOfflineLabels(state.ProjectId, labels);
        await Refresh();
    }

    public async Task UpdateProjectName(string? projectName)
    {
        ProjectName = ResolveOfflineProjectName();
        if (Mode != AnnotationBackendMode.Offline)
        {
            return;
        }

        EnsureOfflineProjectIdentity();
        await Refresh();
    }

    public async Task UpdateTaskStatus(int taskId, AnnotationTaskStatus status)
    {
        if (Mode == AnnotationBackendMode.Offline)
        {
            var annotations = await RetrieveTaskAnnotations(taskId);
            await SaveOfflineTaskAnnotations(taskId, annotations, status);
            await Refresh();
            return;
        }

        await UpdateCvatTaskStatus(taskId, status);
        await Refresh();
    }

    protected override async Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
        if (Mode == AnnotationBackendMode.Offline)
        {
            await RefreshOffline();
        }
        else
        {
            await RefreshCvat();
        }
    }

    private string ResolveServerUrl()
    {
        var uri = new Uri(ServerUrl);
        return uri.Host.Equals("cvat.eyeauras.net", StringComparison.OrdinalIgnoreCase) ? "https://cvat.eyeauras.net" : ServerUrl;
    }

    private async Task RefreshCvat()
    {
        if (string.IsNullOrWhiteSpace(Username) ||
            string.IsNullOrWhiteSpace(Password) ||
            string.IsNullOrWhiteSpace(ServerUrl))
        {
            ClearCachedState();
            return;
        }

        var projects = await cvatClient.RetrieveProjects();
        projectsSources.EditDiff(projects
            .Where(x => x.Id != null)
            .Select(MapProject)
            .ToArray());

        if (ProjectId != default)
        {
            var project = await cvatClient.RetrieveProject(ProjectId);
            ProjectName = project.Name ?? $"Project #{ProjectId}";
            OrganizationId = project.Organization;
        }
        else
        {
            ProjectName = string.Empty;
            OrganizationId = null;
        }

        if (OrganizationId != null)
        {
            var organization = await cvatClient.RetrieveOrganization(OrganizationId.Value);
            OrganizationName = organization.Name;
        }
        else
        {
            OrganizationName = null;
        }

        if (ProjectId == default)
        {
            labelSource.Clear();
            taskSource.Clear();
            jobsSource.Clear();
            projectFileSource.Clear();
            return;
        }

        var labels = await cvatClient.RetrieveLabels(OrganizationId, ProjectId);
        labelSource.EditDiff(labels
            .Where(x => x.Id != null)
            .Select(MapLabel)
            .ToArray());

        var projectTasks = await RetrieveCvatTasks(ProjectId);
        taskSource.EditDiff(projectTasks.Select(MapTask).ToArray());

        var projectJobs = await RetrieveCvatJobs(projectTasks.Select(x => x.Id!.Value));
        jobsSource.EditDiff(projectJobs
            .Where(x => x.Id != null && x.Task_id != null)
            .Select(MapJob)
            .ToArray());

        var projectFiles = await RetrieveCvatProjectFiles(projectTasks.Select(x => x.Id!.Value));
        projectFileSource.EditDiff(projectFiles);
    }

    private async Task RefreshOffline()
    {
        var state = ResolveOfflineProjectIdentity();
        ProjectId = state.ProjectId;
        ProjectName = state.ProjectName;
        OrganizationId = null;
        OrganizationName = null;

        if (CurrentUser == null)
        {
            CurrentUser = new AnnotationUserInfo
            {
                Username = string.IsNullOrWhiteSpace(Username) ? Environment.UserName : Username,
            };
        }

        var importSummary = await EnrichOfflineProjectFromAnnotationFiles(state);
        if (importSummary.HasChanges)
        {
            WhenNotified.OnNext(new NotificationConfig
            {
                NotificationType = NotificationType.Info,
                Message = $"Recovered {importSummary.AddedTasks} task(s) and {importSummary.AddedLabels} label(s) from annotation XML files.",
                Placement = NotificationPlacement.TopRight,
            });
        }

        projectsSources.EditDiff(new[]
        {
            new AnnotationProjectInfoItem
            {
                Id = state.ProjectId,
                Name = state.ProjectName,
            },
        });

        var labels = await ReadOfflineLabels(state.ProjectId);
        labelSource.EditDiff(labels.Select(MapLabel).ToArray());

        var tasks = await ReadOfflineTasks(state.ProjectId);
        taskSource.EditDiff(tasks.Select(MapTask).ToArray());
        jobsSource.EditDiff(tasks.Select(MapJob).ToArray());
        projectFileSource.EditDiff(tasks.SelectMany(x => x.Files.EmptyIfNull().Select(fileName => new TaskFileInfo
        {
            FileName = fileName,
            TaskId = x.TaskId,
        })).ToArray());
    }

    private async Task<OfflineAnnotationImportSummary> EnrichOfflineProjectFromAnnotationFiles(OfflineProjectIdentity state)
    {
        try
        {
            var trainingDirectory = GetOfflineAssetsTrainingDirectory();
            var descriptors = await Task.Run(() => ScanOfflineAnnotationFiles(trainingDirectory, state.ProjectId));
            if (descriptors.Count <= 0)
            {
                return OfflineAnnotationImportSummary.Empty;
            }

            var matchingProjectDescriptors = descriptors
                .Where(x => x.ProjectId == state.ProjectId)
                .ToArray();
            var selectedDescriptors = matchingProjectDescriptors.Length > 0 ? matchingProjectDescriptors : descriptors.ToArray();

            var existingTasks = await ReadOfflineTasks(state.ProjectId);
            if (existingTasks.Count <= 0 && state.ProjectId <= 1)
            {
                var discoveredProjectIds = selectedDescriptors
                    .Where(x => x.ProjectId > 0)
                    .Select(x => x.ProjectId)
                    .Distinct()
                    .ToArray();
                if (discoveredProjectIds.Length == 1)
                {
                    state.ProjectId = discoveredProjectIds[0];
                    ProjectId = state.ProjectId;
                    existingTasks = await ReadOfflineTasks(state.ProjectId);
                    selectedDescriptors = descriptors.Where(x => x.ProjectId == state.ProjectId).ToArray();
                    Log.Info($"Recovered offline project id {state.ProjectId} from annotation XML files");
                }
            }

            var labels = await ReadOfflineLabels(state.ProjectId);
            var labelsByName = labels.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            var existingTaskIds = existingTasks.Select(x => x.TaskId).ToHashSet();
            var nextLabelId = GetNextOfflineLabelId(labels);
            var addedLabels = 0;
            foreach (var label in selectedDescriptors
                         .SelectMany(x => x.Labels)
                         .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                         .Select(x => new { Name = x.Key, Color = x.First().Value }))
            {
                if (labelsByName.ContainsKey(label.Name))
                {
                    continue;
                }

                var labelId = nextLabelId++;
                var labelState = new OfflineLabelState
                {
                    Id = labelId,
                    Name = label.Name,
                    Color = NormalizeLabelColor(label.Color, labelId),
                };
                labels.Add(labelState);
                labelsByName[label.Name] = labelState;
                addedLabels++;
            }

            var newTasks = selectedDescriptors
                .Where(x => x.TaskId > 0 && !existingTaskIds.Contains(x.TaskId) && x.Files.Length > 0)
                .OrderBy(x => x.TaskId)
                .Select(x => new OfflineTaskState
                {
                    TaskId = x.TaskId,
                    Name = string.IsNullOrWhiteSpace(x.TaskName) ? $"Task {x.TaskId}" : x.TaskName,
                    Files = x.Files,
                    Status = AnnotationTaskStatus.Completed,
                    Revision = 1,
                    CreatedAt = x.CreatedAt ?? DateTimeOffset.UtcNow,
                    UpdatedAt = x.UpdatedAt ?? DateTimeOffset.UtcNow,
                    CompletedAt = x.UpdatedAt ?? DateTimeOffset.UtcNow,
                })
                .ToArray();

            if (addedLabels <= 0 && newTasks.Length <= 0)
            {
                return OfflineAnnotationImportSummary.Empty;
            }

            if (addedLabels > 0)
            {
                await SaveOfflineLabels(state.ProjectId, labels);
            }

            foreach (var task in newTasks)
            {
                await SaveOfflineTask(task);
            }

            Log.Info($"Recovered offline project metadata from annotation XML files: {new { AddedTasks = newTasks.Length, AddedLabels = addedLabels, ProjectId = state.ProjectId }}");
            return new OfflineAnnotationImportSummary(newTasks.Length, addedLabels);
        }
        catch (Exception e)
        {
            Log.Warn("Failed to recover offline project metadata from annotation XML files", e);
            return OfflineAnnotationImportSummary.Empty;
        }
    }

    private IReadOnlyList<OfflineAnnotationFileInfo> ScanOfflineAnnotationFiles(DirectoryInfo trainingDirectory, int projectId)
    {
        try
        {
            trainingDirectory.Refresh();
            if (!trainingDirectory.Exists)
            {
                return Array.Empty<OfflineAnnotationFileInfo>();
            }
        }
        catch (Exception e)
        {
            Log.Warn($"Failed to inspect offline annotations directory {trainingDirectory.FullName}", e);
            return Array.Empty<OfflineAnnotationFileInfo>();
        }

        FileInfo[] files;
        try
        {
            var preferredPattern = projectId > 0
                ? $"annotations.project.{projectId}.task.*.xml"
                : "annotations.project.*.task.*.xml";
            files = trainingDirectory.GetFiles(preferredPattern, SearchOption.TopDirectoryOnly);
            if (files.Length <= 0 && projectId > 0)
            {
                files = trainingDirectory.GetFiles("annotations.project.*.task.*.xml", SearchOption.TopDirectoryOnly);
            }
        }
        catch (Exception e)
        {
            Log.Warn($"Failed to enumerate offline annotation XML files under {trainingDirectory.FullName}", e);
            return Array.Empty<OfflineAnnotationFileInfo>();
        }

        var result = new List<OfflineAnnotationFileInfo>();
        foreach (var file in files)
        {
            try
            {
                var parsed = TryReadOfflineAnnotationFile(file);
                if (parsed != null)
                {
                    result.Add(parsed);
                }
            }
            catch (Exception e)
            {
                Log.Warn($"Failed to parse offline annotation XML file {file.FullName}", e);
            }
        }

        return result;
    }

    private OfflineAnnotationFileInfo? TryReadOfflineAnnotationFile(FileInfo file)
    {
        var document = XDocument.Load(file.FullName);
        var taskElement = document.Root?.Element("meta")?.Element("task");
        var fileNameMatch = OfflineAnnotationFileNameRegex.Match(file.Name);
        var projectId = fileNameMatch.Success
            ? ParseInt(fileNameMatch.Groups["projectId"].Value, 0)
            : 0;
        var taskId = ParseInt(taskElement?.Element("id")?.Value, fileNameMatch.Success ? ParseInt(fileNameMatch.Groups["taskId"].Value, 0) : 0);
        if (taskId <= 0)
        {
            return null;
        }

        var labels = document
            .Descendants("label")
            .Select(x => new
            {
                Name = x.Element("name")?.Value?.Trim(),
                Color = x.Element("color")?.Value?.Trim(),
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Concat(document.Descendants("box")
                .Select(x => new
                {
                    Name = x.Attribute("label")?.Value?.Trim(),
                    Color = default(string),
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name)))
            .GroupBy(x => x.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.Select(y => y.Color).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)) ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        var files = document.Root?
                .Elements("image")
                .Select(x => new
                {
                    Id = ParseInt(x.Attribute("id")?.Value, int.MaxValue),
                    Name = x.Attribute("name")?.Value?.Trim(),
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .OrderBy(x => x.Id)
                .Select(x => x.Name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            ?? Array.Empty<string>();

        return new OfflineAnnotationFileInfo(
            file,
            projectId,
            taskId,
            taskElement?.Element("name")?.Value?.Trim() ?? $"Task {taskId}",
            files,
            labels,
            ParseCvatDate(taskElement?.Element("created")?.Value),
            ParseCvatDate(taskElement?.Element("updated")?.Value),
            document.Descendants("box").Any());
    }

    private async Task<IReadOnlyList<TaskRead>> RetrieveCvatTasks(int projectId)
    {
        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var taskClient = new CvatTasksClient(httpClient);
            var tasks = await taskClient.Tasks_listAsync(project_id: projectId, page_size: int.MaxValue);
            return tasks.Results.EmptyIfNull().ToArray();
        });
    }

    private async Task<IReadOnlyList<JobRead>> RetrieveCvatJobs(IEnumerable<int> taskIds)
    {
        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var jobClient = new CvatJobsClient(httpClient);
            var jobs = new List<JobRead>();
            foreach (var taskId in taskIds)
            {
                var taskJobs = await jobClient.Jobs_listAsync(task_id: taskId);
                jobs.AddRange(taskJobs?.Results ?? Array.Empty<JobRead>());
            }

            return jobs;
        });
    }

    private async Task UpdateCvatTaskStatus(int taskId, AnnotationTaskStatus status)
    {
        var jobIds = jobsSource.Items
            .Where(x => x.TaskId == taskId)
            .Select(x => x.Id)
            .Distinct()
            .ToArray();

        if (jobIds.Length <= 0)
        {
            var jobs = await RetrieveCvatJobs(new[] { taskId });
            jobIds = jobs
                .Where(x => x.Id != null)
                .Select(x => x.Id!.Value)
                .Distinct()
                .ToArray();
        }

        if (jobIds.Length <= 0)
        {
            throw new InvalidOperationException($"Task #{taskId} has no CVAT jobs to update");
        }

        var state = MapOperationStatus(status);
        await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var jobClient = new CvatJobsClient(httpClient);
            foreach (var jobId in jobIds)
            {
                await jobClient.Jobs_partial_updateAsync(jobId, new PatchedJobWriteRequest
                {
                    State = state,
                });
            }
        });
    }

    private async Task<IReadOnlyList<TaskFileInfo>> RetrieveCvatProjectFiles(IEnumerable<int> taskIds)
    {
        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var taskClient = new CvatTasksClient(httpClient);
            var projectFiles = new List<TaskFileInfo>();
            foreach (var taskId in taskIds)
            {
                var metadata = await taskClient.Tasks_retrieve_data_metaAsync(taskId);
                if (metadata?.Frames == null)
                {
                    continue;
                }

                projectFiles.AddRange(metadata.Frames.Select(x => new TaskFileInfo
                {
                    FileName = x.Name ?? string.Empty,
                    TaskId = taskId,
                }));
            }

            return projectFiles;
        });
    }

    private async Task<AnnotationTaskInfo> CreateOfflineTask(IReadOnlyList<FileInfo> filesToAdd)
    {
        var state = ResolveOfflineProjectIdentity();
        var existingTasks = await ReadOfflineTasks(state.ProjectId);
        var annotationTaskIds = await Task.Run(() => ScanOfflineAnnotationFiles(GetOfflineAssetsTrainingDirectory(), state.ProjectId)
            .Select(x => x.TaskId)
            .ToArray());
        var taskId = Math.Max(GetNextOfflineTaskId(existingTasks), annotationTaskIds.DefaultIfEmpty(0).Max() + 1);

        var task = new OfflineTaskState
        {
            TaskId = taskId,
            Name = $"Task {idGenerator.Next()}",
            Files = filesToAdd.Select(x => x.Name).ToArray(),
            Status = AnnotationTaskStatus.New,
            Revision = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await SaveOfflineTask(task);
        var taskInfo = MapTask(task);
        UpdateOfflineTaskCache(task);
        await Refresh();
        return taskInfo;
    }

    private async Task<AnnotationDataMeta> RetrieveOfflineMetadata(int taskId)
    {
        var task = await ReadOfflineTask(taskId);
        if (task == null)
        {
            throw new InvalidOperationException($"Offline task {taskId} was not found");
        }

        var frames = task.Files
            .EmptyIfNull()
            .Select((fileName, frameIndex) =>
            {
                var file = ResolveOfflineTaskFile(fileName);
                if (!file.Exists)
                {
                    return new AnnotationFrameInfo
                    {
                        Index = frameIndex,
                        Name = fileName,
                    };
                }

                var imageSize = ImageUtils.GetImageSize(file);
                return new AnnotationFrameInfo
                {
                    Index = frameIndex,
                    Name = fileName,
                    Width = imageSize.Width,
                    Height = imageSize.Height,
                };
            })
            .ToArray();

        return new AnnotationDataMeta
        {
            Frames = frames,
        };
    }

    private async Task SaveOfflineTaskAnnotations(int taskId, IReadOnlyList<CvatRectangleAnnotation> labels, AnnotationTaskStatus status)
    {
        var task = await ReadOfflineTask(taskId);
        if (task == null)
        {
            throw new InvalidOperationException($"Offline task {taskId} was not found");
        }

        var annotationsFile = GetOfflineTaskAnnotationsXmlFile(taskId);
        if (annotationsFile.Directory is { Exists: false })
        {
            annotationsFile.Directory.Create();
        }

        var existingAnnotations = await RetrieveTaskAnnotations(taskId);
        var nextRevision = existingAnnotations.SequenceEqual(labels) && task.Status == status ? task.Revision : task.Revision + 1;

        task.Status = status;
        task.Revision = nextRevision;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        task.CompletedAt = status == AnnotationTaskStatus.Completed ? DateTimeOffset.UtcNow : null;
        await SaveOfflineTask(task);
        UpdateOfflineTaskCache(task);

        var normalizedLabels = labels
            .Select(x => x with
            {
                Source = string.IsNullOrWhiteSpace(x.Source)
                    ? status == AnnotationTaskStatus.Completed ? "manual" : "automatic"
                    : x.Source,
            })
            .ToArray();

        await WriteOfflineTaskAnnotationsXml(task, normalizedLabels);
    }

    private async Task<IReadOnlyList<CvatRectangleAnnotation>> ReadOfflineTaskAnnotationsXml(int taskId, FileInfo annotationsFile)
    {
        var task = await ReadOfflineTask(taskId);
        if (task == null)
        {
            throw new InvalidOperationException($"Offline task {taskId} was not found");
        }

        var document = XDocument.Load(annotationsFile.FullName);
        var labelsByName = (await ReadOfflineLabels(ProjectId))
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var frameIndexesByName = task.Files
            .Select((fileName, index) => new { fileName, index })
            .GroupBy(x => x.fileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().index, StringComparer.OrdinalIgnoreCase);

        var annotations = new List<CvatRectangleAnnotation>();
        foreach (var imageElement in document.Root?.Elements("image") ?? Enumerable.Empty<XElement>())
        {
            var imageName = imageElement.Attribute("name")?.Value ?? string.Empty;
            var frameIndex = frameIndexesByName.GetValueOrDefault(imageName, ParseInt(imageElement.Attribute("id")?.Value, -1));
            if (frameIndex < 0 || frameIndex >= task.Files.Length)
            {
                continue;
            }

            foreach (var boxElement in imageElement.Elements("box"))
            {
                var labelName = boxElement.Attribute("label")?.Value ?? string.Empty;
                if (!labelsByName.TryGetValue(labelName, out var label))
                {
                    continue;
                }

                var left = ParseDouble(boxElement.Attribute("xtl")?.Value);
                var top = ParseDouble(boxElement.Attribute("ytl")?.Value);
                var right = ParseDouble(boxElement.Attribute("xbr")?.Value);
                var bottom = ParseDouble(boxElement.Attribute("ybr")?.Value);
                var width = Math.Max(0, right - left);
                var height = Math.Max(0, bottom - top);
                if (width <= 0 || height <= 0)
                {
                    continue;
                }

                annotations.Add(new CvatRectangleAnnotation
                {
                    Kind = CvatAnnotationShapeKind.Rectangle,
                    FrameIndex = frameIndex,
                    LabelId = label.Id,
                    BoundingBox = new RectangleD(left, top, width, height),
                    RotationDegrees = NormalizeRotation(ParseDouble(boxElement.Attribute("rotation")?.Value)),
                    Source = boxElement.Attribute("source")?.Value,
                });
            }
        }

        return annotations;
    }

    private async Task WriteOfflineTaskAnnotationsXml(OfflineTaskState task, IReadOnlyList<CvatRectangleAnnotation> annotations)
    {
        var outputFile = GetOfflineTaskAnnotationsXmlFile(task.TaskId);
        if (outputFile.Directory is { Exists: false })
        {
            outputFile.Directory.Create();
        }

        var document = await CreateOfflineAnnotationsXml(task, annotations);
        document.Save(outputFile.FullName);
    }

    private async Task ExportOfflineAnnotations(int taskId, FileInfo outputFile)
    {
        var task = await ReadOfflineTask(taskId);
        if (task == null)
        {
            throw new InvalidOperationException($"Offline task {taskId} was not found");
        }

        if (outputFile.Directory is { Exists: false })
        {
            outputFile.Directory.Create();
        }

        var xml = await CreateOfflineAnnotationsXml(task, await RetrieveTaskAnnotations(taskId));
        if (outputFile.Exists)
        {
            outputFile.Delete();
        }

        xml.Save(outputFile.FullName);
    }

    private async Task<XDocument> CreateOfflineAnnotationsXml(OfflineTaskState task, IReadOnlyList<CvatRectangleAnnotation> annotations)
    {
        var labelsById = (await ReadOfflineLabels(ProjectId)).ToDictionary(x => x.Id);
        var annotationsByFrame = annotations
            .GroupBy(x => x.FrameIndex)
            .ToDictionary(x => x.Key, x => x.ToArray());

        var labelElements = labelsById.Values
            .OrderBy(x => x.Name)
            .Select(label => new XElement("label",
                new XElement("name", label.Name),
                new XElement("color", string.IsNullOrWhiteSpace(label.Color) ? PickOfflineLabelColor(label.Id) : label.Color),
                new XElement("type", "any"),
                new XElement("attributes")))
            .ToArray();

        var images = new List<XElement>();
        for (var frameIndex = 0; frameIndex < task.Files.Length; frameIndex++)
        {
            var fileName = task.Files[frameIndex];
            var file = ResolveOfflineTaskFile(fileName);
            var imageSize = file.Exists ? ImageUtils.GetImageSize(file) : new System.Drawing.Size();

            var imageElement = new XElement("image",
                new XAttribute("id", frameIndex),
                new XAttribute("name", fileName),
                new XAttribute("width", imageSize.Width),
                new XAttribute("height", imageSize.Height));

            foreach (var shape in annotationsByFrame.GetValueOrDefault(frameIndex).EmptyIfNull())
            {
                if (!labelsById.TryGetValue(shape.LabelId, out var label))
                {
                    continue;
                }

                imageElement.Add(new XElement("box",
                    new XAttribute("label", label.Name),
                    new XAttribute("source", string.IsNullOrWhiteSpace(shape.Source) ? "manual" : shape.Source),
                    new XAttribute("occluded", 0),
                    new XAttribute("rotation", FormatDouble(NormalizeRotation(shape.RotationDegrees))),
                    new XAttribute("xtl", FormatDouble(shape.BoundingBox.X)),
                    new XAttribute("ytl", FormatDouble(shape.BoundingBox.Y)),
                    new XAttribute("xbr", FormatDouble(shape.BoundingBox.X + shape.BoundingBox.Width)),
                    new XAttribute("ybr", FormatDouble(shape.BoundingBox.Y + shape.BoundingBox.Height)),
                    new XAttribute("z_order", 0)));
            }

            images.Add(imageElement);
        }

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("annotations",
                new XElement("version", "1.1"),
                new XElement("meta",
                    new XElement("task",
                        new XElement("id", task.TaskId),
                        new XElement("name", task.Name),
                        new XElement("size", task.Files.Length),
                        new XElement("mode", "annotation"),
                        new XElement("overlap", 0),
                        new XElement("bugtracker", string.Empty),
                        new XElement("created", FormatCvatDate(task.CreatedAt)),
                        new XElement("updated", FormatCvatDate(task.UpdatedAt)),
                        new XElement("subset", "default"),
                        new XElement("start_frame", 0),
                        new XElement("stop_frame", Math.Max(0, task.Files.Length - 1)),
                        new XElement("frame_filter", string.Empty),
                        new XElement("segments",
                            new XElement("segment",
                                new XElement("id", task.TaskId),
                                new XElement("start", 0),
                                new XElement("stop", Math.Max(0, task.Files.Length - 1)),
                                new XElement("url", $"offline://tasks/{task.TaskId}/jobs/{task.TaskId}"))),
                        new XElement("owner",
                            new XElement("username", CurrentUser?.Username ?? Environment.UserName),
                            new XElement("email", string.Empty)),
                        new XElement("assignee", string.Empty),
                        new XElement("labels", labelElements)),
                    new XElement("dumped", FormatCvatDate(DateTimeOffset.UtcNow))),
                images));
    }

    private static double NormalizeRotation(double rotationDegrees)
    {
        var normalized = rotationDegrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static int ParseInt(string? value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    private static double ParseDouble(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static DateTimeOffset? ParseCvatDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string FormatCvatDate(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.ffffff+00:00", CultureInfo.InvariantCulture);
    }

    private OfflineProjectIdentity ResolveOfflineProjectIdentity()
    {
        EnsureOfflineProjectIdentity();
        return new OfflineProjectIdentity
        {
            ProjectId = ProjectId,
            ProjectName = ProjectName,
        };
    }

    private void EnsureOfflineProjectIdentity()
    {
        if (StorageDirectory == null)
        {
            throw new InvalidOperationException("Storage directory must be initialized before using offline mode");
        }

        var rootDirectory = GetOfflineRootDirectory();
        if (!rootDirectory.Exists)
        {
            rootDirectory.Create();
        }

        ProjectId = ProjectId > 0 ? ProjectId : ResolveOfflineProjectIdFromAnnotationFiles();
        ProjectName = ResolveOfflineProjectName();
    }

    private int ResolveOfflineProjectIdFromAnnotationFiles()
    {
        try
        {
            var trainingDirectory = GetOfflineAssetsTrainingDirectory();
            trainingDirectory.Refresh();
            if (!trainingDirectory.Exists)
            {
                return 1;
            }

            var projectIds = trainingDirectory
                .GetFiles("annotations.project.*.task.*.xml", SearchOption.TopDirectoryOnly)
                .Select(x => OfflineAnnotationFileNameRegex.Match(x.Name))
                .Where(x => x.Success)
                .Select(x => ParseInt(x.Groups["projectId"].Value, 0))
                .Where(x => x > 0)
                .Distinct()
                .ToArray();
            return projectIds.Length == 1 ? projectIds[0] : 1;
        }
        catch (Exception e)
        {
            Log.Warn("Failed to derive offline project id from annotation XML files", e);
            return 1;
        }
    }

    private async Task<List<OfflineLabelState>> ReadOfflineLabels(int projectId)
    {
        var labelsFile = GetOfflineLabelsFile(projectId);
        return await Task.Run(() =>
        {
            try
            {
                if (!labelsFile.Exists)
                {
                    return new List<OfflineLabelState>();
                }

                var content = File.ReadAllText(labelsFile.FullName);
                return configSerializer.Deserialize<List<OfflineLabelState>>(content) ?? new List<OfflineLabelState>();
            }
            catch (Exception e)
            {
                Log.Warn($"Failed to read offline labels from {labelsFile.FullName}", e);
                return new List<OfflineLabelState>();
            }
        });
    }

    private async Task SaveOfflineLabels(int projectId, List<OfflineLabelState> labels)
    {
        var labelsFile = GetOfflineLabelsFile(projectId);
        if (labelsFile.Directory is { Exists: false })
        {
            labelsFile.Directory.Create();
        }

        await File.WriteAllTextAsync(labelsFile.FullName, configSerializer.Serialize(labels));
    }

    private async Task<List<OfflineTaskState>> ReadOfflineTasks(int projectId)
    {
        var tasksDirectory = GetOfflineTasksDirectory(projectId);
        return await Task.Run(() =>
        {
            try
            {
                tasksDirectory.Refresh();
                if (!tasksDirectory.Exists)
                {
                    return new List<OfflineTaskState>();
                }
            }
            catch (Exception e)
            {
                Log.Warn($"Failed to inspect offline tasks directory {tasksDirectory.FullName}", e);
                return new List<OfflineTaskState>();
            }

            FileInfo[] taskFiles;
            try
            {
                taskFiles = tasksDirectory.GetFiles("task.json", SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                Log.Warn($"Failed to enumerate offline task files under {tasksDirectory.FullName}", e);
                return new List<OfflineTaskState>();
            }

            var tasks = new List<OfflineTaskState>();
            foreach (var taskFile in taskFiles)
            {
                try
                {
                    var content = File.ReadAllText(taskFile.FullName);
                    var task = configSerializer.Deserialize<OfflineTaskState>(content);
                    if (task != null)
                    {
                        tasks.Add(task);
                    }
                }
                catch (Exception e)
                {
                    Log.Warn($"Failed to read offline task file {taskFile.FullName}", e);
                }
            }

            return tasks.OrderBy(x => x.TaskId).ToList();
        });
    }

    private async Task<OfflineTaskState?> ReadOfflineTask(int taskId)
    {
        var taskFile = GetOfflineTaskStateFile(taskId);
        if (!taskFile.Exists)
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(taskFile.FullName);
        return configSerializer.Deserialize<OfflineTaskState>(content);
    }

    private async Task SaveOfflineTask(OfflineTaskState task)
    {
        var taskFile = GetOfflineTaskStateFile(task.TaskId);
        if (taskFile.Directory is { Exists: false })
        {
            taskFile.Directory.Create();
        }

        await File.WriteAllTextAsync(taskFile.FullName, configSerializer.Serialize(task));
    }

    private void UpdateOfflineTaskCache(OfflineTaskState task)
    {
        taskSource.AddOrUpdate(MapTask(task));
        jobsSource.AddOrUpdate(MapJob(task));

        var taskFiles = task.Files.EmptyIfNull().Select(fileName => new TaskFileInfo
        {
            FileName = fileName,
            TaskId = task.TaskId,
        }).ToArray();
        var taskFileKeys = taskFiles.Select(GetTaskFileCacheKey).ToHashSet(StringComparer.Ordinal);
        foreach (var staleTaskFile in projectFileSource.Items
                     .Where(x => x.TaskId == task.TaskId && !taskFileKeys.Contains(GetTaskFileCacheKey(x)))
                     .ToArray())
        {
            projectFileSource.RemoveKey(GetTaskFileCacheKey(staleTaskFile));
        }

        projectFileSource.AddOrUpdate(taskFiles);
    }

    private static string GetTaskFileCacheKey(TaskFileInfo taskFile)
    {
        return GetTaskFileCacheKey(taskFile.TaskId, taskFile.FileName);
    }

    private static string GetTaskFileCacheKey(int? taskId, string fileName)
    {
        return $"{taskId}:{fileName}";
    }

    private DirectoryInfo GetOfflineRootDirectory()
    {
        if (StorageDirectory == null)
        {
            throw new InvalidOperationException("Storage directory is not configured");
        }

        return new DirectoryInfo(Path.Combine(StorageDirectory.FullName, "annotation"));
    }

    private DirectoryInfo GetOfflineProjectDirectory(int projectId)
    {
        return GetOfflineRootDirectory();
    }

    private FileInfo GetOfflineLabelsFile(int projectId)
    {
        return new FileInfo(Path.Combine(GetOfflineProjectDirectory(projectId).FullName, "labels.json"));
    }

    private DirectoryInfo GetOfflineTasksDirectory(int projectId)
    {
        return new DirectoryInfo(Path.Combine(GetOfflineProjectDirectory(projectId).FullName, "tasks"));
    }

    private DirectoryInfo GetOfflineTaskDirectory(int taskId)
    {
        return new DirectoryInfo(Path.Combine(GetOfflineTasksDirectory(ProjectId).FullName, $"{taskId}"));
    }

    private FileInfo GetOfflineTaskStateFile(int taskId)
    {
        return new FileInfo(Path.Combine(GetOfflineTaskDirectory(taskId).FullName, "task.json"));
    }

    private FileInfo GetOfflineTaskAnnotationsJsonFile(int taskId)
    {
        return new FileInfo(Path.Combine(GetOfflineTaskDirectory(taskId).FullName, "annotations.json"));
    }

    private FileInfo GetOfflineTaskAnnotationsXmlFile(int taskId)
    {
        return new FileInfo(Path.Combine(GetOfflineAssetsTrainingDirectory().FullName, $"annotations.project.{ProjectId}.task.{taskId}.xml"));
    }

    private DirectoryInfo GetOfflineAssetsTrainingDirectory()
    {
        if (StorageDirectory == null)
        {
            throw new InvalidOperationException("Storage directory is not configured");
        }

        return new DirectoryInfo(Path.Combine(StorageDirectory.FullName, "assets", "training"));
    }

    private FileInfo ResolveOfflineTaskFile(string fileName)
    {
        if (StorageDirectory == null)
        {
            throw new InvalidOperationException("Storage directory is not configured");
        }

        return new FileInfo(Path.Combine(GetOfflineAssetsTrainingDirectory().FullName, fileName));
    }

    private static AnnotationProjectInfoItem MapProject(ProjectRead project)
    {
        return new AnnotationProjectInfoItem
        {
            Id = project.Id!.Value,
            Name = project.Name ?? $"Project #{project.Id}",
        };
    }

    private static AnnotationLabelInfo MapLabel(Label label)
    {
        return new AnnotationLabelInfo
        {
            Id = label.Id!.Value,
            Name = label.Name ?? $"Label #{label.Id}",
            Color = string.IsNullOrWhiteSpace(label.Color) ? "#4f7cff" : label.Color,
        };
    }

    private static AnnotationLabelInfo MapLabel(OfflineLabelState label)
    {
        return new AnnotationLabelInfo
        {
            Id = label.Id,
            Name = label.Name,
            Color = string.IsNullOrWhiteSpace(label.Color) ? PickOfflineLabelColor(label.Id) : label.Color,
        };
    }

    private static AnnotationTaskInfo MapTask(TaskRead task)
    {
        return new AnnotationTaskInfo
        {
            Id = task.Id!.Value,
            Name = task.Name ?? $"Task #{task.Id}",
            Status = MapStatus(task.Status),
            Revision = (int)(task.Updated_date?.UtcTicks ?? task.Created_date?.UtcTicks ?? task.Id ?? 0),
            CreatedAt = task.Created_date,
            UpdatedAt = task.Updated_date,
        };
    }

    private static AnnotationTaskInfo MapTask(OfflineTaskState task)
    {
        return new AnnotationTaskInfo
        {
            Id = task.TaskId,
            Name = task.Name,
            Status = task.Status,
            Revision = task.Revision,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
        };
    }

    private static AnnotationJobInfo MapJob(JobRead job)
    {
        return new AnnotationJobInfo
        {
            Id = job.Id!.Value,
            TaskId = job.Task_id!.Value,
            Name = $"Job #{job.Id}",
            Status = MapStatus(job.Status),
            UpdatedAt = job.Updated_date,
        };
    }

    private static AnnotationJobInfo MapJob(OfflineTaskState task)
    {
        return new AnnotationJobInfo
        {
            Id = task.TaskId,
            TaskId = task.TaskId,
            Name = $"{task.Name} / Job 1",
            Status = task.Status,
            UpdatedAt = task.UpdatedAt,
        };
    }

    private static AnnotationTaskStatus MapStatus(JobStatus? status)
    {
        return status == JobStatus.Completed ? AnnotationTaskStatus.Completed : AnnotationTaskStatus.InProgress;
    }

    private static OperationStatus MapOperationStatus(AnnotationTaskStatus status)
    {
        return status switch
        {
            AnnotationTaskStatus.New => OperationStatus.New,
            AnnotationTaskStatus.InProgress => OperationStatus.In_progress,
            AnnotationTaskStatus.Completed => OperationStatus.Completed,
            _ => OperationStatus.In_progress,
        };
    }

    private static string PickOfflineLabelColor(int labelId)
    {
        return AnnotationLabelPalette.PickByLabelId(labelId);
    }

    private static string NormalizeLabelColor(string? color, int fallbackId)
    {
        if (!string.IsNullOrWhiteSpace(color) &&
            color.StartsWith('#') &&
            (color.Length == 7 || color.Length == 9))
        {
            return color;
        }

        return PickOfflineLabelColor(fallbackId);
    }

    private static int GetNextOfflineLabelId(IEnumerable<OfflineLabelState> labels)
    {
        return labels.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1;
    }

    private static int GetNextOfflineTaskId(IEnumerable<OfflineTaskState> tasks)
    {
        return tasks.Select(x => x.TaskId).DefaultIfEmpty(0).Max() + 1;
    }

    private string ResolveOfflineProjectName()
    {
        if (ProjectFile != null)
        {
            return Path.GetFileNameWithoutExtension(ProjectFile.Name);
        }

        return string.IsNullOrWhiteSpace(ProjectName) ? "Offline Project" : ProjectName.Trim();
    }

    private void ClearCachedState()
    {
        projectsSources.Clear();
        labelSource.Clear();
        taskSource.Clear();
        jobsSource.Clear();
        projectFileSource.Clear();
        OrganizationId = null;
        OrganizationName = null;
        if (Mode == AnnotationBackendMode.Cvat)
        {
            ProjectName = string.Empty;
        }
    }

    /// <summary>
    /// Summarizes metadata recovered from existing CVAT annotation XML files.
    /// </summary>
    private sealed record OfflineAnnotationImportSummary(int AddedTasks, int AddedLabels)
    {
        public static OfflineAnnotationImportSummary Empty { get; } = new(0, 0);

        public bool HasChanges => AddedTasks > 0 || AddedLabels > 0;
    }

    private sealed record OfflineAnnotationFileInfo(
        FileInfo File,
        int ProjectId,
        int TaskId,
        string TaskName,
        string[] Files,
        IReadOnlyDictionary<string, string> Labels,
        DateTimeOffset? CreatedAt,
        DateTimeOffset? UpdatedAt,
        bool HasAnnotations);

    /// <summary>
    /// Represents offline project identity derived from the project file and workspace contents.
    /// </summary>
    private sealed record OfflineProjectIdentity
    {
        public int ProjectId { get; set; }

        public string ProjectName { get; set; } = "Offline Project";
    }

    /// <summary>
    /// Persists one offline label with its stable id and display color.
    /// </summary>
    private sealed record OfflineLabelState
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Color { get; init; } = "#4f7cff";
    }

    /// <summary>
    /// Persists one offline task and its metadata needed to rebuild task lists between sessions.
    /// </summary>
    private sealed record OfflineTaskState
    {
        public int TaskId { get; init; }

        public string Name { get; set; } = string.Empty;

        public AnnotationTaskStatus Status { get; set; }

        public string[] Files { get; set; } = Array.Empty<string>();

        public int Revision { get; set; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; set; }

        public DateTimeOffset? CompletedAt { get; set; }
    }

    /// <summary>
    /// Persists the per-task annotation XML payload and revision counter for offline tasks.
    /// </summary>
    private sealed record OfflineTaskAnnotationsState
    {
        public int Revision { get; init; }

        public List<CvatRectangleAnnotation> Shapes { get; init; } = new();
    }
}
