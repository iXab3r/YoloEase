using System.Linq;
using CvatApi;
using PoeShared.Services;
using YoloEase.UI.Dto;
using YoloEase.UI.Scaffolding;

namespace YoloEase.UI.Core;

public class CvatProjectAccessor : RefreshableReactiveObject
{
    private static readonly Binder<CvatProjectAccessor> Binder = new();

    private readonly ICvatClient cvatClient;
    private readonly IUniqueIdGenerator idGenerator;
    private readonly SourceCacheEx<TaskFileInfo, string> projectFileSource = new(x => x.FileName); // potential clash
    private readonly SourceCacheEx<JobRead, int> jobsSource = new(x => x.Id.Value);
    private readonly SourceCacheEx<TaskRead, int> taskSource = new(x => x.Id.Value);
    private readonly SourceCacheEx<ProjectRead, int> projectsSources = new(x => x.Id.Value);
    private readonly SourceCacheEx<Label, int> labelSource = new(x => x.Id.Value);

    private OrganizationRead organizationRead;

    static CvatProjectAccessor()
    {
        Binder.Bind(x => x.Username).To(x => x.cvatClient.Username);
        Binder.Bind(x => x.Password).To(x => x.cvatClient.Password);
        Binder.Bind(x => x.ServerUrl).To(x => x.cvatClient.ServerUrl);
        Binder.Bind(x => x.ProjectId != 0 && !string.IsNullOrEmpty(x.ProjectName)).To(x => x.IsReady);
    }

    public CvatProjectAccessor(ICvatClient cvatClient, IUniqueIdGenerator idGenerator)
    {
        this.cvatClient = cvatClient;
        this.idGenerator = idGenerator;
        Binder.Attach(this).AddTo(Anchors);
    }

    public string Username { get; set; } = "test";

    public string Password { get; set; }

    public string ServerUrl { get; set; } = "https://cvat.eyeauras.net";

    public int ProjectId { get; set; }
    
    public int? OrganizationId { get; private set; }

    public string OrganizationName { get; private set; }

    public string ProjectName { get; private set; }

    public bool IsReady { get; private set; }

    public ICvatClient Client => cvatClient;
    
    public IObservableCacheEx<JobRead, int> Jobs => jobsSource;

    public IObservableCacheEx<TaskRead, int> Tasks => taskSource;

    public IObservableCacheEx<TaskFileInfo, string> ProjectFiles => projectFileSource;

    public IObservableCacheEx<Label, int> Labels => labelSource;
    
    public IObservableCacheEx<ProjectRead, int> Projects => projectsSources;
    
    public MetaUser CurrentUser { get; private set; }
    
    public async Task Logout()
    {
        await Client.Api.Logout();
        CurrentUser = default;
    }
    
    public async Task Login()
    {
        CurrentUser = await Client.Api.Login();
    }

    private string ResolveServerUrl()
    {
        var uri = new Uri(ServerUrl);
        return uri.Host.Equals("cvat.eyeauras.net") ? "https://cvat.eyeauras.net" : ServerUrl;
    }

    protected override async Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
        var projects = await cvatClient.RetrieveProjects();
        projectsSources.EditDiff(projects);

        if (ProjectId != default)
        {
            var project = await cvatClient.RetrieveProject(ProjectId);
            ProjectName = project.Name;
            OrganizationId = project.Organization;
        }
        else
        {
            ProjectName = default;
            OrganizationId = default;
        }
        
        if (OrganizationId != null)
        {
            var organization = await cvatClient.RetrieveOrganization(OrganizationId.Value);
            OrganizationName = organization.Name;
        }
        else
        {
            OrganizationName = default;
        }

        if (ProjectId == default)
        {
            labelSource.Clear();
            taskSource.Clear();
            jobsSource.Clear();
            projectFileSource.Clear();
        }
        else
        {
            var labels = await cvatClient.RetrieveLabels(OrganizationId, ProjectId);
            labelSource.EditDiff(labels);
            
            var projectTasks = await RetrieveTasks(cvatClient, ProjectId);
            taskSource.EditDiff(projectTasks);
        
            var projectJobs = await RetrieveJobs(cvatClient, projectTasks.Select(x => x.Id.Value));
            jobsSource.EditDiff(projectJobs);
        
            var projectFiles = await RetrieveProjectFiles(cvatClient, projectTasks.Select(x => x.Id.Value));
            projectFileSource.EditDiff(projectFiles);
        }
    }

    public async Task DeleteTask(int taskId)
    {
        await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var taskClient = new CvatTasksClient(httpClient);
            await taskClient.Tasks_destroyAsync(id: taskId);
            taskSource.RemoveKey(taskId);
        });
    }

    public async Task NavigateToTask(int taskId)
    {
        var job = jobsSource.Items.FirstOrDefault(x => x.Task_id == taskId);

        var relativePath = job == null ? $"tasks/{taskId}" : $"tasks/{taskId}/jobs/{job.Id}";
        await ProcessUtils.OpenUri($"{ResolveServerUrl()}/{relativePath}");
    }

    public string ResolveProjectUrl(int projectId)
    {
        var relativePath = $"projects/{projectId}";
        return $"{ResolveServerUrl()}/{relativePath}";
    }

    public async Task NavigateToProject(int projectId)
    {
        var projectUrl = ResolveProjectUrl(projectId);
        await ProcessUtils.OpenUri(projectUrl);
    }

    public static async Task<IReadOnlyList<TaskRead>> RetrieveTasks(ICvatClient cvatClient, int projectId)
    {
        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var taskClient = new CvatTasksClient(httpClient);
            var tasks = await taskClient.Tasks_listAsync(project_id: projectId, page_size: int.MaxValue);

            return tasks.Results.EmptyIfNull().ToList();
        });
    }

    private static async Task<IReadOnlyList<JobRead>> RetrieveJobs(ICvatClient cvatClient, IEnumerable<int> tasks)
    {
        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var jobClient = new CvatJobsClient(httpClient);
            var jobs = new List<JobRead>();
            
            foreach (var taskId in tasks)
            {
                var taskJobs = await jobClient.Jobs_listAsync(task_id: taskId);
                if (taskJobs == null)
                {
                    throw new InvalidOperationException($"Failed to get jobs of a task {taskId}");
                }

                jobs.AddRange(taskJobs.Results ?? Array.Empty<JobRead>());
            }

            return jobs;
        });
    }

    public async Task<DataMetaRead> RetrieveMetadata(int taskId)
    {
        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var taskClient = new CvatTasksClient(httpClient);
            var metadata = await taskClient.Tasks_retrieve_data_metaAsync(taskId);
            if (metadata == null)
            {
                throw new InvalidOperationException($"Failed to get metadata of a task {taskId}");
            }
            if (metadata.Frames == null)
            {
                throw new InvalidOperationException($"Failed to get frames of a task {taskId}: {metadata}");
            }


            return metadata;
        });
    }

    private static async Task<IReadOnlyList<TaskFileInfo>> RetrieveProjectFiles(ICvatClient cvatClient, IEnumerable<int> tasks)
    {
        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var taskClient = new CvatTasksClient(httpClient);

            var projectFiles = new List<TaskFileInfo>();
            foreach (var taskId in tasks)
            {

                var metadata = await taskClient.Tasks_retrieve_data_metaAsync(taskId);
                if (metadata == null)
                {
                    throw new InvalidOperationException($"Failed to get metadata of a task {taskId}");
                }

                if (metadata.Frames == null)
                {
                    continue;
                }

                var taskFiles = metadata.Frames.Select(x => new TaskFileInfo()
                {
                    FileName = x.Name,
                    TaskId = taskId
                }).ToArray();
                projectFiles.AddRange(taskFiles);
            }

            return projectFiles;
        });
    }

    public async Task<TaskRead> CreateTask(IReadOnlyList<FileInfo> filesToAdd)
    {
        var task = await CreateTaskViaCli(filesToAdd);
        taskSource.AddOrUpdate(task);
        return task;
    }

    private async Task<TaskRead> CreateTaskViaCli(IReadOnlyList<FileInfo> filesToAdd)
    {
        if (filesToAdd.Count <= 0)
        {
            throw new ArgumentException("There must be at least one file in the next batch");
        }
        
        var taskId = await cvatClient.Cli.CreateTask(projectId: ProjectId, organization: OrganizationName, taskName: $"Task {idGenerator.Next()}", filesToUpload: filesToAdd);
        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var taskClient = new CvatTasksClient(httpClient);
            var task = await taskClient.Tasks_retrieveAsync(taskId);
            return task;
        });
    }
}