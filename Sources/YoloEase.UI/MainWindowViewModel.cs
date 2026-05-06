using System.Linq;
using System.Reactive;
using System.Threading;
using AntDesign;
using JetBrains.Annotations;
using LiteDB;
using Microsoft.Extensions.FileProviders;
using PoeShared.Blazor.Scaffolding;
using PoeShared.Common;
using PoeShared.Dialogs.Services;
using PoeShared.Scaffolding.WPF;
using PoeShared.Services;
using Polly;
using YoloEase.UI.Augmentations;
using YoloEase.UI.Controls;
using YoloEase.UI.Core;
using YoloEase.UI.Prerequisites;
using YoloEase.UI.Prism;
using YoloEase.UI.TaskAnnotation;
using YoloEase.UI.TrainingTimeline;

namespace YoloEase.UI;

/// <summary>
/// Drives the main desktop shell, project lifecycle, recent projects, and docked tab state.
/// </summary>
public class MainWindowViewModel : RefreshableReactiveObject, ICanBeSelected
{
    private static readonly Binder<MainWindowViewModel> Binder = new();
    private static readonly TimeSpan RetiredProjectDisposalPollDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RetiredProjectDisposalMaxDelay = TimeSpan.FromSeconds(30);

    private readonly IOpenFileDialog openFileDialog;
    private readonly ISaveFileDialog saveProjectFileDialog;
    private readonly IOpenFileDialog openProjectFileDialog;
    private readonly IAppArguments appArguments;
    private readonly IConfigSerializer configSerializer;
    private readonly IApplicationAccessor applicationAccessor;
    private readonly IFactory<YoloEaseProject> projectFactory;
    private readonly IScheduler uiScheduler;
    private readonly SemaphoreSlim projectLifecycleGate = new(1, 1);

    private readonly ISourceList<IFileInfo> additionalFilesSource = new SourceList<IFileInfo>();
    private readonly ISourceCache<TabItem, string> tabsSource = new SourceCache<TabItem, string>(x => x.Id);

    private readonly TabItem settingsTab;
    private readonly TabItem prerequisitesTab;
    private readonly TabItem annotationsTab;
    private readonly TabItem tasksTab;
    private readonly TabItem trainerTab;
    private readonly TabItem augmentationsTab;
    private readonly TabItem localTab;
    private readonly TabItem remoteTab;
    private readonly TabItem batchTab;
    private readonly TabItem trainingTab;
    private readonly FileInfo databaseFile;
    private bool prerequisitesActivationPending;
    
    static MainWindowViewModel()
    {
        Binder.Bind(x => GetProjectDirectory(x)).To(x => x.ProjectDirectory);
        Binder.Bind(x => GetStorageDirectory(x)).To(x => x.StorageDirectory);
        Binder.Bind(x => GetProjectOutputDirectory(x)).To(x => x.ProjectOutputDirectory);
        Binder.Bind(x => x.ProjectOutputDirectory).To((x, v) => x.ApplyProjectStorageDirectory(v));

        Binder.Bind(x => GetProjectId(x)).To(x => x.ProjectId);
        Binder.Bind(x => x.ProjectId).To((x, v) => x.ApplyProjectId(v));
        Binder.Bind(x => x.YoloEaseProject).To((x, v) => x.AutomaticTrainer.Project = v);
        Binder.Bind(x => GetProjectAssets(x)).To((x,v) => x.localTab.DataContext = v);
        Binder.Bind(x => GetProjectRemoteProject(x)).To((x,v) => x.remoteTab.DataContext = v);
        Binder.Bind(x => GetProjectTrainingBatch(x)).To((x,v) => x.batchTab.DataContext = v);
        Binder.Bind(x => GetProjectAugmentations(x)).To((x,v) => x.augmentationsTab.DataContext = v);
        Binder.Bind(x => x.YoloEaseProject).To((x,v) => x.annotationsTab.DataContext = v);
        Binder.Bind(x => x.YoloEaseProject).To((x,v) => x.tasksTab.DataContext = v);
        Binder.Bind(x => GetProjectTrainingDataset(x)).To((x,v) => x.trainingTab.DataContext = v);

        Binder.Bind(x => GetLoadedProjectShortPath(x)).To(x => x.LoadedProjectShortPath!);

        Binder
            .Bind(x =>
                $"YoloEase {x.appArguments.Version}{(x.YoloEaseProject.IsEmpty ? "" : $" | {x.YoloEaseProject.RemoteProject.ProjectName}")}{(string.IsNullOrEmpty(x.LoadedProjectShortPath) ? "" : $" | {x.LoadedProjectShortPath}")}")
            .To(x => x.Title);
    }

    public MainWindowViewModel(
        IOpenFileDialog openFileDialog,
        IOpenFileDialog openProjectFileDialog,
        ISaveFileDialog saveProjectFileDialog,
        IAppArguments appArguments,
        AutomaticTrainer automaticTrainer,
        PrerequisitesViewModel prerequisites,
        IConfigSerializer configSerializer,
        IApplicationAccessor applicationAccessor,
        IFactory<YoloEaseProject> projectFactory,
        [Dependency(WellKnownSchedulers.UI)] IScheduler uiScheduler)
    {
        AutomaticTrainer = automaticTrainer.AddTo(Anchors);
        Prerequisites = prerequisites.AddTo(Anchors);
        AppArguments = appArguments;
        this.openFileDialog = openFileDialog;
        this.appArguments = appArguments;
        this.configSerializer = configSerializer;
        this.applicationAccessor = applicationAccessor;
        this.projectFactory = projectFactory;
        this.uiScheduler = uiScheduler;
        YoloEaseProject = CreateEmptyProject();

        this.databaseFile = new FileInfo(Path.Combine(appArguments.AppDataDirectory, "database.sqlite"));

        this.openProjectFileDialog = openProjectFileDialog.AddTo(Anchors);
        openProjectFileDialog.Filter = "YoloEase project|*.yeproj|All files|*.*";
        openProjectFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        this.saveProjectFileDialog = saveProjectFileDialog.AddTo(Anchors);
        saveProjectFileDialog.Filter = "YoloEase project|*.yeproj|All files|*.*";
        saveProjectFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        settingsTab = new TabItem()
        {
            Title = "Settings",
            DataContext = this,
            ViewType = typeof(ProjectTree.ProjectSettings),
            SortOrder = 0
        }.AddTo(tabsSource);

        prerequisitesTab = new TabItem()
        {
            Title = "Prerequisites",
            DataContext = Prerequisites,
            ViewType = typeof(PrerequisitesView),
            SortOrder = -10
        }.AddTo(tabsSource);

        annotationsTab = new TabItem()
        {
            Title = "Project",
            ViewType = typeof(AnnotationsEditor),
            SortOrder = 10
        }.AddTo(tabsSource);

        tasksTab = new TabItem()
        {
            Title = "Tasks",
            ViewType = typeof(TasksList),
            SortOrder = 20
        }.AddTo(tabsSource);

        trainerTab = new TabItem()
        {
            Title = "Trainer",
            DataContext = AutomaticTrainer,
            ViewType = typeof(AutomaticTrainerView),
            SortOrder = 30
        }.AddTo(tabsSource);    
        
        augmentationsTab = new TabItem()
        {
            Title = "Augmentations",
            ViewType = typeof(AugmentationsEditor),
            SortOrder = 40
        }.AddTo(tabsSource);

        localTab = new TabItem()
        {
            Title = "Local",
            SortOrder = 50
        }.AddTo(tabsSource);

        remoteTab = new TabItem()
        {
            Title = "Remote",
            SortOrder = 60
        }.AddTo(tabsSource);

        batchTab = new TabItem()
        {
            Title = "Batch",
            SortOrder = 70
        }.AddTo(tabsSource);

        trainingTab = new TabItem()
        {
            Title = "Training",
            SortOrder = 80
        }.AddTo(tabsSource);

        this.WhenAnyValue(x => x.IsAdvancedMode)
            .CombineLatest(
                this.WhenAnyValue(x => x.YoloEaseProject).Select(project => !project.IsEmpty),
                (isAdvancedMode, isProjectLoaded) => new { isAdvancedMode, isProjectLoaded })
            .Subscribe(x =>
            {
                annotationsTab.Title = "Project";
                settingsTab.IsVisible = x.isProjectLoaded;
                annotationsTab.IsVisible = x.isProjectLoaded;
                prerequisitesTab.IsVisible = x.isProjectLoaded;
                tasksTab.IsVisible = x.isProjectLoaded;
                trainerTab.IsVisible = x.isProjectLoaded;
                augmentationsTab.IsVisible = x.isProjectLoaded;
                localTab.IsVisible = batchTab.IsVisible = trainingTab.IsVisible = x.isProjectLoaded && x.isAdvancedMode;
                remoteTab.IsVisible = false;

                var activeTab = tabsSource.Items.FirstOrDefault(y => y.Id == ActiveTabId);
                if (!x.isProjectLoaded)
                {
                    ActiveTabId = null;
                    return;
                }

                if (activeTab == null || !activeTab.IsVisible)
                {
                    ActiveTabId = settingsTab.Id;
                }
            })
            .AddTo(Anchors);

        Tabs = tabsSource.AsObservableList().AddTo(Anchors);

        this.WhenAnyValue(x => x.ActiveTabId)
            .Subscribe(tabId =>
            {
                var tabs = tabsSource.Items.ToDictionary(x => x.Id);
                if (!string.IsNullOrEmpty(tabId) && tabs.TryGetValue(tabId, out var tab))
                {
                    foreach (var kvp in tabs.Values.Except(new[] {tab}))
                    {
                        kvp.IsSelected = false;
                    }

                    tab.IsSelected = true;
                    if (ReferenceEquals(tab, prerequisitesTab))
                    {
                        Prerequisites.NotifyTabActivated().AndForget(ignoreExceptions: true);
                    }
                }
                else
                {
                    foreach (var kvp in tabs.Values)
                    {
                        kvp.IsSelected = false;
                    }
                }
            })
            .AddTo(Anchors);

        Prerequisites
            .WhenAnyValue(x => x.HasMissingRequired)
            .Where(x => x)
            .Subscribe(_ => ActivatePrerequisitesOrDefer())
            .AddTo(Anchors);

        ProjectsDirectory = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "YoloEase projects"));
        if (ProjectsDirectory.Exists)
        {
            try
            {
                Log.Info($"Creating projects directory @ {ProjectsDirectory.FullName}");
                ProjectsDirectory.Create();
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to create projects directory @ {ProjectsDirectory.FullName}", ex);
            }
        }
        
        var configSource = Observable.Merge(
                ObserveProjectConfigChanges(),
                AutomaticTrainer.WhenAnyValue(x => x.ModelStrategy).ToUnit(),
                AutomaticTrainer.WhenAnyValue(x => x.PickStrategy).ToUnit())
            .Where(_ => !YoloEaseProject.IsEmpty)
            .Sample(TimeSpan.FromSeconds(5))
            .Skip(1) //skip first one as it will be auto-generated
            .Select(x => PrepareProjectConfig());
        
        configSource
            .CombineLatest(this.WhenAnyValue(x => x.LoadedProjectFile), (config, file) => new { config, file })
            .Where(x => x.file != null)
            .EnableIf(this.WhenAnyValue(x => x.LoadedProjectFile).Select(x => x != null))
            .Subscribe(x => SaveProjectConfig(x.config, x.file))
            .AddTo(Anchors);

        additionalFilesSource
            .Connect()
            .BindToCollection(out var additionalFiles)
            .Subscribe()
            .AddTo(Anchors);
        AdditionalFiles = additionalFiles;
        
        this.WhenAnyValue(x => x.YoloEaseProject)
            .Where(x => !x.IsEmpty)
            .Subscribe(x =>
            {
                TryActivatePendingPrerequisites();

                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(750));
                        await Prerequisites.RequestStartupCheckAsync();
                    }
                    catch (Exception e)
                    {
                        Log.Warn("Failed to run delayed prerequisite startup check", e);
                    }
                }).AndForget();

                if (x.RemoteProject.Mode == AnnotationBackendMode.Offline)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await x.RemoteProject.Login();

                        }
                        catch (Exception e)
                        {
                            WhenNotified.OnNext(new NotificationConfig()
                            {
                                NotificationType = NotificationType.Error,
                                Message = $"Failed to prepare offline workspace: {e.Message}",
                                Placement = NotificationPlacement.TopRight,
                            });
                        }
                    });
                }
            })
            .AddTo(Anchors);

        this.WhenAnyValue(x => x.ProjectId)
            .Select(_ => YoloEaseProject)
            .Where(project => !project.IsEmpty &&
                              (project.RemoteProject.Mode == AnnotationBackendMode.Offline ||
                               project.RemoteProject.CurrentUser != null))
            .SubscribeAsync(async project =>
            {
                try
                {
                    await project.RemoteProject.Refresh();
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

        additionalFilesSource.Add(new RefFileInfo(@"https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css"));
        additionalFilesSource.Add(new RefFileInfo(@"_content/Blazor.Bootstrap/blazor.bootstrap.css"));
        additionalFilesSource.Add(new RefFileInfo(@"https://cdnjs.cloudflare.com/ajax/libs/Chart.js/4.0.1/chart.umd.js"));
        additionalFilesSource.Add(new RefFileInfo(@"https://cdnjs.cloudflare.com/ajax/libs/chartjs-plugin-datalabels/2.2.0/chartjs-plugin-datalabels.min.js"));
        additionalFilesSource.Add(new RefFileInfo(@"https://cdn.jsdelivr.net/npm/sortablejs@latest/Sortable.min.js"));
        additionalFilesSource.Add(new RefFileInfo(@"_content/Blazor.Bootstrap/blazor.bootstrap.js"));
        
        additionalFilesSource.Add(new RefFileInfo(@"assets/js/split.min.js"));
        additionalFilesSource.Add(new RefFileInfo(@"assets/js/main.js"));

        NewProjectCommand = CommandWrapper.Create(NewProjectCommandExecuted);
        SaveProjectCommand = CommandWrapper.Create(SaveProjectCommandExecuted);
        SaveAsProjectCommand = CommandWrapper.Create(SaveAsProjectCommandExecuted);
        LoadProjectCommand = CommandWrapper.Create<object>(LoadProjectCommandExecuted);
        CloseProjectCommand = CommandWrapper.Create(CloseProjectCommandExecuted);
        ExitAppCommand = CommandWrapper.Create(ExitAppCommandExecuted);

        RecentProjects = LoadRecentProjects().ToSourceList();

        Binder.Attach(this).AddTo(Anchors);
    }

    private IObservable<Unit> ObserveProjectConfigChanges()
    {
        return this.WhenAnyValue(x => x.YoloEaseProject)
            .Select(project => project.IsEmpty
                ? Observable.Empty<Unit>()
                : Observable.Merge(
                    Observable.Return(Unit.Default),
                    this.WhenAnyValue(x => x.ProjectId).ToUnit(),
                    project.RemoteProject.WhenAnyValue(x => x.Mode).ToUnit(),
                    project.RemoteProject.WhenAnyValue(x => x.ProjectName).ToUnit(),
                    project.RemoteProject.WhenAnyValue(x => x.Username).ToUnit(),
                    this.WhenAnyValue(x => x.StorageProjectSubfolder).ToUnit(),
                    project.TrainingBatch.WhenAnyValue(x => x.BatchPercentage).ToUnit(),
                    project.TrainingDataset.WhenAnyValue(x => x.BaseModelPath).ToUnit(),
                    project.TrainingDataset.WhenAnyValue(x => x.Epochs).ToUnit(),
                    project.TrainingDataset.WhenAnyValue(x => x.ModelSize).ToUnit(),
                    project.TrainingDataset.WhenAnyValue(x => x.TrainValSplitPercentage).ToUnit(),
                    project.TrainingDataset.WhenAnyValue(x => x.TrainAdditionalArguments).ToUnit(),
                    project.TrainingDataset.WhenAnyValue(x => x.MaxNumberOfCpuCores).ToUnit(),
                    project.Predictions.WhenAnyValue(x => x.ConfidenceThresholdPercentage).ToUnit(),
                    project.Predictions.WhenAnyValue(x => x.IoUThresholdPercentage).ToUnit(),
                    project.Predictions.WhenAnyValue(x => x.PredictAdditionalArguments).ToUnit(),
                    project.Predictions.WhenAnyValue(x => x.PredictionModel).ToUnit(),
                    project.AutoAnnotation.Models.Connect().AutoRefresh().ToUnit(),
                    project.AutoAnnotation.Models.Connect().MergeMany(model => model.LabelMappings.Connect().AutoRefresh().ToUnit()),
                    project.Augmentations.Effects.Connect().AutoRefresh().ToUnit(),
                    project.DataSources.InputDirectories.Connect().ToUnit()))
            .Switch();
    }

    public string Title { get; [UsedImplicitly] private set; }

    public IAppArguments AppArguments { get; }
    
    public ISourceList<RecentProjectInfo> RecentProjects { get; }

    public IReadOnlyObservableCollection<IFileInfo> AdditionalFiles { get; }

    public IObservableList<TabItem> Tabs { get; }

    public YoloEaseProject YoloEaseProject { get; private set; } = null!;

    public int ProjectId { get; set; }

    public AutomaticTrainer AutomaticTrainer { get; }

    public PrerequisitesViewModel Prerequisites { get; }
    
    public AugmentationsAccessor Augmentations { get; }

    public string? LoadedProjectShortPath { get; [UsedImplicitly] private set; }

    public DirectoryInfo? StorageDirectory { get; [UsedImplicitly] private set; }

    public DirectoryInfo? ProjectDirectory { get; [UsedImplicitly] private set; }

    public DirectoryInfo? ProjectOutputDirectory { get; [UsedImplicitly] private set; }

    public string? StorageProjectSubfolder { get; private set; }

    public string? PendingStorageProjectSubfolder { get; set; }

    public string? StorageProjectSubfolderError { get; private set; }

    public DirectoryInfo ProjectsDirectory { get; }

    public bool IsAdvancedMode { get; set; }

    public string ActiveTabId { get; set; }

    public bool IsSelected { get; set; }

    public ICommandWrapper NewProjectCommand { get; }

    public ICommandWrapper SaveProjectCommand { get; }

    public ICommandWrapper SaveAsProjectCommand { get; }

    public ICommandWrapper LoadProjectCommand { get; }

    public ICommandWrapper CloseProjectCommand { get; }

    public ICommandWrapper ExitAppCommand { get; }

    public FileInfo? LoadedProjectFile { get; private set; }

    public GeneralProperties? LoadedProject { get; private set; }

    private YoloEaseProject CreateEmptyProject()
    {
        var project = projectFactory.Create();
        project.IsEmpty = true;
        project.RemoteProject.ProjectId = 0;
        project.RemoteProject.ProjectName = string.Empty;
        return project;
    }

    private void DisposeRetiredProject(YoloEaseProject? project)
    {
        if (project == null || ReferenceEquals(project, YoloEaseProject))
        {
            return;
        }

        Log.Info($"Scheduling retired project disposal: {project}");
        Task.Run(async () =>
        {
            try
            {
                var startedAt = DateTime.UtcNow;
                while (project.IsBusy || AutomaticTrainer.IsBusy)
                {
                    if (DateTime.UtcNow - startedAt >= RetiredProjectDisposalMaxDelay)
                    {
                        Log.Warn($"Retired project is still busy after {RetiredProjectDisposalMaxDelay}; leaving it undisposed: {project}");
                        return;
                    }

                    await Task.Delay(RetiredProjectDisposalPollDelay);
                }

                uiScheduler.Schedule(() =>
                {
                    try
                    {
                        if (ReferenceEquals(project, YoloEaseProject))
                        {
                            Log.Debug($"Skipping retired project disposal because it is active again: {project}");
                            return;
                        }

                        Log.Info($"Disposing retired project instance: {project}");
                        project.Dispose();
                    }
                    catch (Exception e)
                    {
                        Log.Warn($"Failed to dispose retired project instance: {project}", e);
                    }
                });
            }
            catch (Exception e)
            {
                Log.Warn($"Failed while scheduling retired project disposal: {project}", e);
            }
        }).AndForget();
    }

    private void AddRecentProject(RecentProjectInfo projectInfo)
    {
        Policy.Handle<Exception>(ex =>
        {
            Log.Warn($"Exception occured when attempted to get access to database: {databaseFile}");
            return true;
        }).WaitAndRetry(new[]
        {
            TimeSpan.FromSeconds(0.1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
        }).Execute(() =>
        {
            Log.Info($"Trying to add project {projectInfo} to {databaseFile}");
            using var database = new LiteDatabase(databaseFile.FullName);
            var collection = database.GetCollection<RecentProjectInfo>("RecentProjects");
            collection.DeleteMany(x => x.FilePath == projectInfo.FilePath);
            collection.Insert(projectInfo);
        });
    }
    
    private RecentProjectInfo[] LoadRecentProjects()
    {
        var recentProjects = 
            Policy.Handle<Exception>(ex =>
            {
                Log.Warn($"Exception occured when attempted to get access to database: {databaseFile}");
                return true;
            }).WaitAndRetry(new[]
            {
                TimeSpan.FromSeconds(0.1),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
            }).Execute(() =>
            {
                Log.Info($"Trying to acquire recent projects from {databaseFile}");
                if (!File.Exists(databaseFile.FullName))
                {
                    return Array.Empty<RecentProjectInfo>();
                }
                using var database = new LiteDatabase(databaseFile.FullName);

                var collection = database.GetCollection<RecentProjectInfo>("RecentProjects");
                return collection.FindAll().OrderByDescending(x => x.AccessTime).ToArray();
            });
        return recentProjects;
    }

    private async Task NewProjectCommandExecuted()
    {
        try
        {
            var loaded = LoadedProjectFile;
            if (loaded != null)
            {
                saveProjectFileDialog.InitialDirectory = loaded.DirectoryName;
                saveProjectFileDialog.InitialFileName = loaded.Name;
            }
            else
            {
                saveProjectFileDialog.InitialDirectory = ProjectsDirectory.FullName;
                saveProjectFileDialog.InitialFileName = "project.yeproj";
            }
        
        
            var newConfig = new GeneralProperties
            {
                AnnotationBackendMode = AnnotationBackendMode.Offline,
            };
            var loadedConfig = LoadedProject;
            if (loadedConfig != null)
            {
                newConfig.Username = loadedConfig.Username;
                newConfig.ProjectName = loadedConfig.ProjectName;
            }

            var selectedFile = saveProjectFileDialog.ShowDialog();
            if (selectedFile != null && SaveProjectConfig(newConfig, selectedFile))
            {
                await OpenProjectFile(selectedFile);
            }
        }
        catch (Exception e)
        {
            HandleExternalOperationFailure("Failed to create project", e);
        }
    }

    private async Task SaveProjectCommandExecuted()
    {
        try
        {
            var loaded = LoadedProjectFile;
            if (loaded == null)
            {
                await SaveAsProjectCommand.ExecuteAsync(null);
            }
            else
            {
                var config = PrepareProjectConfig();
                SaveProjectConfig(config, loaded);
            }
        }
        catch (Exception e)
        {
            HandleExternalOperationFailure("Failed to save project", e);
        }
    }

    private async Task SaveAsProjectCommandExecuted()
    {
        try
        {
            var loaded = LoadedProjectFile;
            if (loaded != null)
            {
                saveProjectFileDialog.InitialDirectory = loaded.DirectoryName;
                saveProjectFileDialog.InitialFileName = loaded.Name;
            }
            else
            {
                saveProjectFileDialog.InitialDirectory = ProjectsDirectory.FullName;
                saveProjectFileDialog.InitialFileName = "project.yeproj";
            }

            var selectedFile = saveProjectFileDialog.ShowDialog();
            if (selectedFile != null)
            {
                var previousLoadedProjectFile = LoadedProjectFile;
                LoadedProjectFile = selectedFile;
                ApplyLoadedProjectFileContext();
                var config = PrepareProjectConfig();
                if (!SaveProjectConfig(config, selectedFile))
                {
                    LoadedProjectFile = previousLoadedProjectFile;
                    ApplyLoadedProjectFileContext();
                }
            }
        }
        catch (Exception e)
        {
            HandleExternalOperationFailure("Failed to save project as", e);
        }
    }

    private async Task LoadProjectCommandExecuted(object arg)
    {
        uiScheduler.Schedule(async () =>
        {
            try
            {
                FileInfo selectedFile;
                if (arg is RecentProjectInfo recentProjectInfo)
                {
                    selectedFile = new FileInfo(recentProjectInfo.FilePath);
                }
                else
                {
                    var loaded = LoadedProjectFile;
                    if (loaded != null)
                    {
                        openProjectFileDialog.InitialDirectory = loaded.DirectoryName;
                        openProjectFileDialog.InitialFileName = loaded.Name;
                    }
                    else
                    {
                        openProjectFileDialog.InitialDirectory = ProjectsDirectory.FullName;
                    }

                    selectedFile = openProjectFileDialog.ShowDialog();
                }

                if (selectedFile != null)
                {
                    await OpenProjectFile(selectedFile);
                }
            }
            catch (Exception e)
            {
                HandleExternalOperationFailure("Failed to open project", e);
            }
        });
    }

    private async Task CloseProjectCommandExecuted()
    {
        await projectLifecycleGate.WaitAsync();
        try
        {
            await CloseProjectCore();
        }
        finally
        {
            projectLifecycleGate.Release();
        }
    }

    private async Task CloseProjectCore()
    {
        var project = YoloEaseProject;
        if (project.IsEmpty)
        {
            return;
        }

        try
        {
            if (LoadedProjectFile != null)
            {
                SaveProjectConfig(PrepareProjectConfig(), LoadedProjectFile);
            }
        }
        catch (Exception e)
        {
            Log.Warn($"Failed to save project before closing {LoadedProjectFile}", e);
            WhenNotified.OnNext(new NotificationConfig
            {
                NotificationType = NotificationType.Warning,
                Message = $"Failed to save project before closing: {e.Message}",
                Placement = NotificationPlacement.TopRight,
            });
        }

        Log.Info($"Closing project {LoadedProjectFile}");
        try
        {
            await AutomaticTrainer.Stop();
        }
        catch (Exception e)
        {
            Log.Warn($"Failed to stop trainer while closing project {LoadedProjectFile}", e);
        }

        YoloEaseProject = CreateEmptyProject();
        LoadedProjectFile = null;
        LoadedProject = null;
        LoadedProjectShortPath = null;
        StorageDirectory = null;
        ProjectDirectory = null;
        ProjectOutputDirectory = null;
        StorageProjectSubfolder = null;
        PendingStorageProjectSubfolder = null;
        StorageProjectSubfolderError = null;
        ProjectId = 0;
        ActiveTabId = null;
        prerequisitesActivationPending = false;
        DisposeRetiredProject(project);
    }

    private void ExitAppCommandExecuted()
    {
        Log.Info("Closing application");
        applicationAccessor.Exit();
    }

    private void HandleExternalOperationFailure(string message, Exception exception)
    {
        Log.Warn(message, exception);
        WhenNotified.OnNext(new NotificationConfig
        {
            NotificationType = NotificationType.Error,
            Message = $"{message}: {exception.Message}",
            Placement = NotificationPlacement.TopRight,
        });
    }

    private void ActivatePrerequisitesOrDefer()
    {
        if (YoloEaseProject.IsEmpty)
        {
            prerequisitesActivationPending = true;
            return;
        }

        ActiveTabId = prerequisitesTab.Id;
    }

    private void TryActivatePendingPrerequisites()
    {
        if (!prerequisitesActivationPending || YoloEaseProject.IsEmpty)
        {
            return;
        }

        prerequisitesActivationPending = false;
        ActiveTabId = prerequisitesTab.Id;
    }

    private static DirectoryInfo? GetProjectDirectory(MainWindowViewModel viewModel)
    {
        return viewModel.LoadedProjectFile?.Directory;
    }

    private static DirectoryInfo? GetStorageDirectory(MainWindowViewModel viewModel)
    {
        return ProjectPathResolver.ResolveStorageDirectory(viewModel.LoadedProjectFile, viewModel.StorageProjectSubfolder);
    }

    private static DirectoryInfo? GetProjectOutputDirectory(MainWindowViewModel viewModel)
    {
        return ProjectPathResolver.ResolveStorageDirectory(viewModel.LoadedProjectFile, viewModel.StorageProjectSubfolder);
    }

    private static string? GetLoadedProjectShortPath(MainWindowViewModel viewModel)
    {
        var loadedProjectFile = viewModel.LoadedProjectFile;
        if (loadedProjectFile?.DirectoryName == null)
        {
            return null;
        }

        return Path.Combine(loadedProjectFile.DirectoryName.TakeMidChars(16, false), loadedProjectFile.Name);
    }

    private static int GetProjectId(MainWindowViewModel viewModel)
    {
        return viewModel.YoloEaseProject.IsEmpty ? 0 : viewModel.YoloEaseProject.RemoteProject.ProjectId;
    }

    private static object GetProjectAssets(MainWindowViewModel viewModel)
    {
        return viewModel.YoloEaseProject.Assets;
    }

    private static object GetProjectRemoteProject(MainWindowViewModel viewModel)
    {
        return viewModel.YoloEaseProject.RemoteProject;
    }

    private static object GetProjectTrainingBatch(MainWindowViewModel viewModel)
    {
        return viewModel.YoloEaseProject.TrainingBatch;
    }

    private static object GetProjectAugmentations(MainWindowViewModel viewModel)
    {
        return viewModel.YoloEaseProject.Augmentations;
    }

    private static object GetProjectTrainingDataset(MainWindowViewModel viewModel)
    {
        return viewModel.YoloEaseProject.TrainingDataset;
    }

    private void ApplyProjectStorageDirectory(DirectoryInfo? projectOutputDirectory)
    {
        if (YoloEaseProject.IsEmpty || projectOutputDirectory == null)
        {
            return;
        }

        YoloEaseProject.StorageDirectory = projectOutputDirectory;
    }

    private void ApplyProjectId(int projectId)
    {
        var project = YoloEaseProject;
        if (project.IsEmpty)
        {
            return;
        }

        project.RemoteProject.ProjectId = projectId;
    }

    private static DirectoryInfo? ParseToDirectoryOrDefault(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return default;
        }

        var directory = new DirectoryInfo(path);
        return directory;
    }

    public async Task OpenProject()
    {
        try
        {
            var project = YoloEaseProject;
            if (project.IsEmpty)
            {
                throw new InvalidOperationException("No project is loaded");
            }

            await project.RemoteProject.NavigateToProject(project.RemoteProject.ProjectId);
        }
        catch (Exception e)
        {
            HandleExternalOperationFailure("Failed to open project workspace", e);
        }
    }

    public async Task OpenAppDirectory()
    {
        try
        {
            await ProcessUtils.OpenFolder(new FileInfo(appArguments.ApplicationExecutablePath).Directory);
        }
        catch (Exception e)
        {
            HandleExternalOperationFailure("Failed to open app directory", e);
        }
    }

    public async Task OpenAppDataDirectory()
    {
        try
        {
            await ProcessUtils.OpenFolder(new DirectoryInfo(appArguments.AppDataDirectory));
        }
        catch (Exception e)
        {
            HandleExternalOperationFailure("Failed to open app data directory", e);
        }
    }

    public async Task OpenStorage()
    {
        try
        {
            if (ProjectOutputDirectory == null)
            {
                throw new InvalidOperationException("Project storage directory is not available");
            }

            await ProcessUtils.OpenFolder(ProjectOutputDirectory);
        }
        catch (Exception e)
        {
            HandleExternalOperationFailure("Failed to open project storage directory", e);
        }
    }

    public async Task ApplyStorageProjectSubfolder()
    {
        try
        {
            var loadedProjectFile = LoadedProjectFile ?? throw new InvalidOperationException("Save or load a project before changing storage.");
            if (!ProjectPathResolver.TryNormalizeStorageProjectSubfolder(PendingStorageProjectSubfolder, out var normalizedSubfolder, out var error))
            {
                StorageProjectSubfolderError = error;
                throw new InvalidOperationException(error);
            }

            StorageProjectSubfolderError = null;
            StorageProjectSubfolder = normalizedSubfolder;
            PendingStorageProjectSubfolder = normalizedSubfolder;
            ApplyLoadedProjectFileContext();

            var project = YoloEaseProject;
            if (project.IsEmpty)
            {
                return;
            }

            Log.Info($"Repointing project storage to {ProjectPathResolver.ResolveStorageDirectory(loadedProjectFile, normalizedSubfolder)?.FullName}");
            try
            {
                await project.Refresh();
            }
            catch (Exception e)
            {
                Log.Warn($"Storage was repointed to {normalizedSubfolder}, but project refresh failed", e);
                WhenNotified.OnNext(new NotificationConfig
                {
                    NotificationType = NotificationType.Warning,
                    Message = $"Storage was repointed, but refresh failed: {e.Message}",
                    Placement = NotificationPlacement.TopRight,
                });
            }
        }
        catch (Exception e)
        {
            HandleExternalOperationFailure("Failed to apply project storage subfolder", e);
        }
    }

    public async Task SelectModel()
    {
        uiScheduler.Schedule(() =>
        {
            try
            {
                var project = YoloEaseProject;
                if (project.IsEmpty)
                {
                    throw new InvalidOperationException("No project is loaded");
                }

                openFileDialog.InitialFileName = project.TrainingDataset.BaseModelPath;
                if (openFileDialog.ShowDialog() != null)
                {
                    project.TrainingDataset.BaseModelPath = openFileDialog.LastFile.FullName;
                }
            }
            catch (Exception e)
            {
                HandleExternalOperationFailure("Failed to select model file", e);
            }
        });
    }

    public async Task<bool> OpenProjectFile(FileInfo file)
    {
        await projectLifecycleGate.WaitAsync();
        try
        {
            file.Refresh();
            if (!file.Exists)
            {
                throw new FileNotFoundException("Project file does not exist", file.FullName);
            }

            if (!string.Equals(file.Extension, ".yeproj", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported project file type: {file.Extension}");
            }

            Log.Info($"Opening project file {file.FullName}");
            return await LoadProjectConfig(file);
        }
        catch (Exception e)
        {
            HandleExternalOperationFailure($"Failed to open project {file.FullName}", e);
            return false;
        }
        finally
        {
            projectLifecycleGate.Release();
        }
    }

   
    private Task<bool> LoadProjectConfig(FileInfo file)
    {
        var previousLoadedProjectFile = LoadedProjectFile;
        var previousLoadedProject = LoadedProject;
        var previousProject = YoloEaseProject;
        var previousActiveTabId = ActiveTabId;
        var previousStorageDirectory = StorageDirectory;
        var previousProjectDirectory = ProjectDirectory;
        var previousProjectOutputDirectory = ProjectOutputDirectory;
        var previousStorageProjectSubfolder = StorageProjectSubfolder;
        var previousPendingStorageProjectSubfolder = PendingStorageProjectSubfolder;
        var previousStorageProjectSubfolderError = StorageProjectSubfolderError;
        YoloEaseProject? replacementProject = null;
        try
        {
            if (!file.Exists)
            {
                throw new FileNotFoundException("Project file does not exist", file.FullName);
            }

            var configJson = File.ReadAllText(file.FullName);
            var config = configSerializer.Deserialize<GeneralProperties>(configJson);
            config = ProjectPathResolver.PrepareLoadedConfig(config, file);
            config = PrepareOfflineProjectConfig(config, file);

            var storageDirectory = ProjectPathResolver.ResolveStorageDirectory(file, config.StorageProjectSubfolder);
            if (storageDirectory != null && Directory.Exists(storageDirectory.FullName) == false)
            {
                try
                {
                    Directory.CreateDirectory(storageDirectory.FullName);
                }
                catch (Exception e)
                {
                    Log.Warn($"Failed to create project storage directory {storageDirectory.FullName}", e);
                }
            }

            LoadedProjectFile = file;
            StorageProjectSubfolder = config.StorageProjectSubfolder;
            PendingStorageProjectSubfolder = config.StorageProjectSubfolder;
            StorageProjectSubfolderError = null;
            replacementProject = CreateProjectFromConfig(config);
            LoadedProject = config;
            YoloEaseProject = replacementProject;
            ApplyLoadedProjectFileContext();
            ActiveTabId = settingsTab.Id;
            AddRecentProject(new RecentProjectInfo
            {
                FilePath = file.FullName,
                AccessTime = DateTime.Now
            });
            DisposeRetiredProject(previousProject);
            return Task.FromResult(true);
        }
        catch (Exception e)
        {
            YoloEaseProject = previousProject;
            DisposeRetiredProject(replacementProject);
            LoadedProjectFile = previousLoadedProjectFile;
            LoadedProject = previousLoadedProject;
            ActiveTabId = previousActiveTabId;
            StorageDirectory = previousStorageDirectory;
            ProjectDirectory = previousProjectDirectory;
            ProjectOutputDirectory = previousProjectOutputDirectory;
            StorageProjectSubfolder = previousStorageProjectSubfolder;
            PendingStorageProjectSubfolder = previousPendingStorageProjectSubfolder;
            StorageProjectSubfolderError = previousStorageProjectSubfolderError;
            ApplyLoadedProjectFileContext();
            HandleExternalOperationFailure($"Failed to load project {file.FullName}", e);
            return Task.FromResult(false);
        }
    }
    
    private YoloEaseProject CreateProjectFromConfig(GeneralProperties config)
    {
        config = PrepareOfflineProjectConfig(config, LoadedProjectFile);

        var project = projectFactory.Create();
        project.IsEmpty = false;
        project.RemoteProject.Mode = AnnotationBackendMode.Offline;
        project.RemoteProject.Username = config.Username;
        project.RemoteProject.Password = string.Empty;
        project.RemoteProject.ServerUrl = string.Empty;
        project.RemoteProject.ProjectId = config.ProjectId;
        project.RemoteProject.ProjectName = LoadedProjectFile != null
            ? Path.GetFileNameWithoutExtension(LoadedProjectFile.Name)
            : config.ProjectName;
        project.DataSources.InputDirectories.EditDiff(config.DataDirectoryPaths
            .EmptyIfNull()
            .Where(x => !string.IsNullOrEmpty(x))
            .Select(x => LoadedProjectFile == null ? new DirectoryInfo(x) : ProjectPathResolver.ResolveDirectoryPathForLoad(x, LoadedProjectFile)));
        project.TrainingDataset.BaseModelPath = LoadedProjectFile == null
            ? config.BaseModelPath
            : ProjectPathResolver.ResolveModelPathForLoad(config.BaseModelPath, LoadedProjectFile);
        project.TrainingDataset.Epochs = config.TrainingEpochs;
        project.TrainingDataset.ModelSize = config.ModelSize;
        project.TrainingDataset.TrainValSplitPercentage = config.TrainValSplitPercentage;
        project.TrainingBatch.BatchPercentage = config.BatchPercentage;
        project.TrainingDataset.TrainAdditionalArguments = config.TrainAdditionalArguments;
        project.TrainingDataset.MaxNumberOfCpuCores = config.MaxNumberOfCpuCores;
        project.Predictions.ConfidenceThresholdPercentage = config.PredictConfidenceThresholdPercentage;
        project.Predictions.IoUThresholdPercentage = config.PredictIoUThresholdPercentage;
        project.Predictions.PredictAdditionalArguments = config.PredictAdditionalArguments;
        var predictionModelPath = !string.IsNullOrWhiteSpace(config.PredictionModelPath)
            ? config.PredictionModelPath
            : config.AutoAnnotationModelPath;
        if (!string.IsNullOrEmpty(predictionModelPath))
        {
            project.Predictions.LoadModel(new FileInfo(LoadedProjectFile == null
                ? predictionModelPath
                : ProjectPathResolver.ResolveProjectPathForLoad(predictionModelPath, LoadedProjectFile)));
        }

        AutomaticTrainer.ModelStrategy = !string.IsNullOrWhiteSpace(config.PredictionModelPath)
            ? config.PredictionModelStrategy
            : config.AutoAnnotateModelStrategy;
        AutomaticTrainer.PredictionStrategy = config.PredictionStrategy;
        AutomaticTrainer.PredictIncludeAnnotated = config.PredictIncludeAnnotated;
        AutomaticTrainer.PredictBatchPercentage = config.PredictBatchPercentage;
        AutomaticTrainer.AutoAnnotate = false;
        AutomaticTrainer.AutoAnnotateConfidenceThresholdPercentage = 25;

        var autoAnnotationModels = ResolveAutoAnnotationModels(config);
        project.AutoAnnotation.LoadModels(autoAnnotationModels);
        if (!project.AutoAnnotation.Models.Items.Any())
        {
            project.AutoAnnotation.AddLatestModel();
        }

        var effects = config.Augmentations.EmptyIfNull()
            .Select(x =>
            {
                switch (x.Value)
                {
                    case RotateImageEffectProperties properties:
                    {
                        return new RotateImageEffect()
                        {
                            Properties = properties
                        };
                    }
                    case FlipImageEffectProperties properties:
                    {
                        return new FlipImageEffect()
                        {
                            Properties = properties
                        };
                    }
                    case BoxBlurImageEffectProperties properties:
                    {
                        return new BoxBlurImageEffect()
                        {
                            Properties = properties
                        };
                    }
                    case NoiseImageEffectProperties properties:
                    {
                        return new NoiseImageEffect()
                        {
                            Properties = properties
                        };
                    }
                }
                return default(IImageEffect);
            })
            .Where(x => x != null)
            .ToArray();
        
        project.Augmentations.Effects.Clear();
        project.Augmentations.Effects.EditDiff(effects);

        ApplyProjectFileContext(project, LoadedProjectFile);
        return project;
    }

    private GeneralProperties PrepareProjectConfig()
    {
        var project = YoloEaseProject;
        if (project.IsEmpty)
        {
            throw new InvalidOperationException("No project is loaded");
        }

        var projectFile = LoadedProjectFile;
        var projectName = LoadedProjectFile != null
            ? Path.GetFileNameWithoutExtension(LoadedProjectFile.Name)
            : project.RemoteProject.ProjectName;
        var baseConfig = LoadedProject ?? new GeneralProperties();
        var storageProjectSubfolder = StorageProjectSubfolder;
        if (projectFile != null && !ProjectPathResolver.TryNormalizeStorageProjectSubfolder(storageProjectSubfolder, out storageProjectSubfolder, out _))
        {
            storageProjectSubfolder = ProjectPathResolver.ResolveDefaultStorageProjectSubfolder(new GeneralPropertiesV0
            {
                AnnotationBackendMode = AnnotationBackendMode.Offline,
                ProjectId = project.RemoteProject.ProjectId,
            }, projectFile);
        }

        var updatedConfig = baseConfig with
        {
            Version = 2,
            StorageProjectSubfolder = storageProjectSubfolder ?? string.Empty,
            AnnotationBackendMode = AnnotationBackendMode.Offline,
            Username = project.RemoteProject.Username,
            Password = string.Empty,
            ServerUrl = string.Empty,
            ProjectId = project.RemoteProject.ProjectId,
            ProjectName = projectName,
            DataDirectoryPaths = project.DataSources.InputDirectories.Items.Select(x => x.FullName).ToArray(),
            TrainingEpochs = project.TrainingDataset.Epochs,
            ModelSize = project.TrainingDataset.ModelSize,
            TrainValSplitPercentage = project.TrainingDataset.TrainValSplitPercentage,
            BatchPercentage = project.TrainingBatch.BatchPercentage,
            BaseModelPath = project.TrainingDataset.BaseModelPath,
            TrainAdditionalArguments = project.TrainingDataset.TrainAdditionalArguments,
            MaxNumberOfCpuCores = project.TrainingDataset.MaxNumberOfCpuCores,
            PredictionModelPath = project.Predictions.PredictionModel?.ModelFile?.FullName ?? string.Empty,
            PredictionModelStrategy = AutomaticTrainer.ModelStrategy,
            PredictionStrategy = AutomaticTrainer.PredictionStrategy,
            PredictIncludeAnnotated = AutomaticTrainer.PredictIncludeAnnotated,
            PredictBatchPercentage = AutomaticTrainer.PredictBatchPercentage,
            AutoAnnotationIsEnabled = false,
            AutoAnnotationModelPath = string.Empty,
            AutoAnnotateConfidenceThresholdPercentage = 25,
            AutoAnnotateModelStrategy = AutomaticTrainerModelStrategy.Latest,

            PredictConfidenceThresholdPercentage = project.Predictions.ConfidenceThresholdPercentage,
            PredictIoUThresholdPercentage = project.Predictions.IoUThresholdPercentage,
            PredictAdditionalArguments = project.Predictions.PredictAdditionalArguments,
            AutoAnnotationModels = project.AutoAnnotation.SaveModels().ToList(),
            
            Augmentations = SaveEffects(project.Augmentations.Effects.Items)
        };
        return projectFile == null ? updatedConfig : ProjectPathResolver.PrepareConfigForSave(updatedConfig, projectFile);
    }

    private static GeneralProperties PrepareOfflineProjectConfig(GeneralProperties config, FileInfo? projectFile)
    {
        var projectName = projectFile == null ? config.ProjectName : Path.GetFileNameWithoutExtension(projectFile.Name);
        return config with
        {
            AnnotationBackendMode = AnnotationBackendMode.Offline,
            Password = string.Empty,
            ServerUrl = string.Empty,
            ProjectName = string.IsNullOrWhiteSpace(projectName) ? config.ProjectName : projectName
        };
    }

    private static IReadOnlyList<AutoAnnotationModelProperties> ResolveAutoAnnotationModels(GeneralProperties config)
    {
        var configuredModels = config.AutoAnnotationModels.EmptyIfNull().ToArray();
        if (configuredModels.Length > 0)
        {
            return configuredModels;
        }

        if (!config.AutoAnnotationIsEnabled && string.IsNullOrWhiteSpace(config.AutoAnnotationModelPath))
        {
            return Array.Empty<AutoAnnotationModelProperties>();
        }

        return new[]
        {
            new AutoAnnotationModelProperties
            {
                Id = Guid.NewGuid().ToString("N"),
                Order = 0,
                DisplayName = config.AutoAnnotateModelStrategy == AutomaticTrainerModelStrategy.Latest
                    ? "Latest"
                    : Path.GetFileNameWithoutExtension(config.AutoAnnotationModelPath) ?? "Custom model",
                SourceKind = config.AutoAnnotateModelStrategy == AutomaticTrainerModelStrategy.Latest
                    ? AutoAnnotationModelSourceKind.Latest
                    : AutoAnnotationModelSourceKind.CustomOnnx,
                OriginalPath = config.AutoAnnotationModelPath,
                OriginalFileName = string.IsNullOrWhiteSpace(config.AutoAnnotationModelPath)
                    ? null
                    : Path.GetFileName(config.AutoAnnotationModelPath),
                IsEnabled = config.AutoAnnotationIsEnabled,
                ConfidenceThresholdPercentage = config.AutoAnnotateConfidenceThresholdPercentage <= 0
                    ? 25
                    : config.AutoAnnotateConfidenceThresholdPercentage,
                IoUThresholdPercentage = config.PredictIoUThresholdPercentage <= 0 ? 70 : config.PredictIoUThresholdPercentage,
            }
        };
    }

    private static List<PoeConfigMetadata<IPoeEyeConfigVersioned>>? SaveEffects(IEnumerable<IImageEffect> effects)
    {
        return effects
            .Select(x => new PoeConfigMetadata<IPoeEyeConfigVersioned>(x.Properties))
            .ToList();
    }

    private bool SaveProjectConfig(GeneralProperties config, FileInfo file)
    {
        try
        {
            if (file.Directory is { Exists: false })
            {
                file.Directory.Create();
            }

            config = PrepareOfflineProjectConfig(config, file);
            var normalizedConfig = ProjectPathResolver.PrepareConfigForSave(config, file);
            var configJson = configSerializer.Serialize(normalizedConfig);
            File.WriteAllText(file.FullName, configJson);
            return true;
        }
        catch (Exception e)
        {
            HandleExternalOperationFailure($"Failed to save project {file.FullName}", e);
            return false;
        }
    }

    private void ApplyLoadedProjectFileContext()
    {
        if (YoloEaseProject.IsEmpty)
        {
            return;
        }

        ApplyProjectFileContext(YoloEaseProject, LoadedProjectFile);
    }

    private void ApplyProjectFileContext(YoloEaseProject project, FileInfo? projectFile)
    {
        project.RemoteProject.SetProjectFile(projectFile);

        if (projectFile != null && string.IsNullOrWhiteSpace(StorageProjectSubfolder))
        {
            StorageProjectSubfolder = ProjectPathResolver.ResolveDefaultStorageProjectSubfolder(new GeneralPropertiesV0
            {
                AnnotationBackendMode = AnnotationBackendMode.Offline,
                ProjectId = project.RemoteProject.ProjectId,
            }, projectFile);
            PendingStorageProjectSubfolder = StorageProjectSubfolder;
        }

        var storageDirectory = ProjectPathResolver.ResolveStorageDirectory(projectFile, StorageProjectSubfolder);
        StorageDirectory = storageDirectory;

        var projectDirectory = storageDirectory;
        if (projectDirectory == null)
        {
            ProjectOutputDirectory = null;
            return;
        }

        ProjectOutputDirectory = projectDirectory;
        project.StorageDirectory = projectDirectory;
    }

    private void SaveProjectConfig(FileInfo file)
    {
        var updatedConfig = PrepareProjectConfig();
        SaveProjectConfig(updatedConfig, file);
    }

    protected override Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
        throw new NotImplementedException();
    }
}
