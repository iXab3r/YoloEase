using System.Linq;
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
using YoloEase.UI.Prism;
using YoloEase.UI.TrainingTimeline;

namespace YoloEase.UI;

public class MainWindowViewModel : RefreshableReactiveObject, ICanBeSelected
{
    private static readonly Binder<MainWindowViewModel> Binder = new();

    private readonly IFolderBrowserDialog folderBrowserDialog;
    private readonly IOpenFileDialog openFileDialog;
    private readonly ISaveFileDialog saveProjectFileDialog;
    private readonly IOpenFileDialog openProjectFileDialog;
    private readonly IAppArguments appArguments;
    private readonly IConfigSerializer configSerializer;
    private readonly IApplicationAccessor applicationAccessor;
    private readonly IFactory<YoloEaseProject> projectFactory;
    private readonly IScheduler uiScheduler;

    private readonly ISourceList<IFileInfo> additionalFilesSource = new SourceList<IFileInfo>();
    private readonly ISourceCache<TabItem, string> tabsSource = new SourceCache<TabItem, string>(x => x.Id);

    private readonly TabItem settingsTab;
    private readonly TabItem trainerTab;
    private readonly TabItem augmentationsTab;
    private readonly TabItem localTab;
    private readonly TabItem remoteTab;
    private readonly TabItem batchTab;
    private readonly TabItem annotationsTab;
    private readonly TabItem trainingTab;
    private readonly FileInfo databaseFile;
    
    static MainWindowViewModel()
    {
        Binder.Bind(x => x.LoadedProjectFile != null && x.LoadedProjectFile.Directory != null ? x.LoadedProjectFile.Directory.GetSubdirectory("storage")  : default).To(x => x.StorageDirectory);
        Binder.BindIf(x => x.YoloEaseProject != null, x => PrepareProjectDirectory(x.StorageDirectory, x.YoloEaseProject!.RemoteProject.ProjectId, x.YoloEaseProject.RemoteProject.ServerUrl)).To(x => x.ProjectOutputDirectory);
        Binder.BindIf(x => x.YoloEaseProject != null, x => x.ProjectOutputDirectory).To(x => x.YoloEaseProject!.StorageDirectory);

        Binder.BindIf(x => x.YoloEaseProject != null, x => x.YoloEaseProject!.RemoteProject.ProjectId).To(x => x.ProjectId);
        Binder.BindIf(x => x.YoloEaseProject != null, x => x.ProjectId).To(x => x.YoloEaseProject!.RemoteProject.ProjectId);
        Binder.BindIf(x => x.YoloEaseProject != null, x => x.YoloEaseProject).To(x => x.AutomaticTrainer.Project);
        Binder.BindIf(x => x.YoloEaseProject != null, x => x.YoloEaseProject!.Assets).To((x,v) => x.localTab.DataContext = v);
        Binder.BindIf(x => x.YoloEaseProject != null, x => x.YoloEaseProject!.RemoteProject).To((x,v) => x.remoteTab.DataContext = v);
        Binder.BindIf(x => x.YoloEaseProject != null, x => x.YoloEaseProject!.TrainingBatch).To((x,v) => x.batchTab.DataContext = v);
        Binder.BindIf(x => x.YoloEaseProject != null, x => x.YoloEaseProject!.Augmentations).To((x,v) => x.augmentationsTab.DataContext = v);
        Binder.BindIf(x => x.YoloEaseProject != null, x => x.YoloEaseProject!.Annotations).To((x,v) => x.annotationsTab.DataContext = v);
        Binder.BindIf(x => x.YoloEaseProject != null,x => x.YoloEaseProject!.TrainingDataset).To((x,v) => x.trainingTab.DataContext = v);

        Binder.BindIf(x => x.LoadedProjectFile != null, x => $"{Path.Combine(x.LoadedProjectFile!.DirectoryName.TakeMidChars(16, false), x.LoadedProjectFile.Name)}")
            .Else(x => default!)
            .To(x => x.LoadedProjectShortPath!);

        Binder
            .Bind(x =>
                $"YoloEase {x.appArguments.Version}{(x.YoloEaseProject == null ? "" : $" | {x.YoloEaseProject.RemoteProject.ProjectName}")}{(string.IsNullOrEmpty(x.LoadedProjectShortPath) ? "" : $" | {x.LoadedProjectShortPath}")}")
            .To(x => x.Title);
    }

    public MainWindowViewModel(
        IFolderBrowserDialog folderBrowserDialog,
        IOpenFileDialog openFileDialog,
        IOpenFileDialog openProjectFileDialog,
        ISaveFileDialog saveProjectFileDialog,
        IAppArguments appArguments,
        AutomaticTrainer automaticTrainer,
        IConfigSerializer configSerializer,
        IApplicationAccessor applicationAccessor,
        IFactory<YoloEaseProject> projectFactory,
        [Dependency(WellKnownSchedulers.UI)] IScheduler uiScheduler)
    {
        AutomaticTrainer = automaticTrainer.AddTo(Anchors);
        AppArguments = appArguments;
        this.folderBrowserDialog = folderBrowserDialog;
        this.openFileDialog = openFileDialog;
        this.appArguments = appArguments;
        this.configSerializer = configSerializer;
        this.applicationAccessor = applicationAccessor;
        this.projectFactory = projectFactory;
        this.uiScheduler = uiScheduler;

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
            DataContext = this
        }.AddTo(tabsSource);

        trainerTab = new TabItem()
        {
            Title = "Trainer",
            DataContext = AutomaticTrainer
        }.AddTo(tabsSource);    
        
        augmentationsTab = new TabItem()
        {
            Title = "Augmentations",
        }.AddTo(tabsSource);

        localTab = new TabItem()
        {
            Title = "Local",
        }.AddTo(tabsSource);

        remoteTab = new TabItem()
        {
            Title = "Remote",
        }.AddTo(tabsSource);

        batchTab = new TabItem()
        {
            Title = "Batch",
        }.AddTo(tabsSource);

        annotationsTab = new TabItem()
        {
            Title = "Annotations",
        }.AddTo(tabsSource);

        trainingTab = new TabItem()
        {
            Title = "Training",
        }.AddTo(tabsSource);

        this.WhenAnyValue(x => x.IsAdvancedMode)
            .Subscribe(x => { localTab.IsVisible = remoteTab.IsVisible = batchTab.IsVisible = annotationsTab.IsVisible = trainingTab.IsVisible = x; })
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
                this.WhenAnyValue(x => x.ProjectId).ToUnit(),
                AutomaticTrainer.WhenAnyValue(x => x.AutoAnnotate).ToUnit(),
                AutomaticTrainer.WhenAnyValue(x => x.ModelStrategy).ToUnit(),
                AutomaticTrainer.WhenAnyValue(x => x.PickStrategy).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.RemoteProject.Username).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.RemoteProject.ServerUrl).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.RemoteProject.Password).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.TrainingBatch.BatchPercentage).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.TrainingDataset.BaseModelPath).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.TrainingDataset.Epochs).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.TrainingDataset.ModelSize).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.TrainingDataset.TrainValSplitPercentage).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.TrainingDataset.TrainAdditionalArguments).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.Predictions.ConfidenceThresholdPercentage).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.Predictions.IoUThresholdPercentage).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.Predictions.PredictAdditionalArguments).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.Predictions.PredictionModel).ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.Augmentations.Effects).Switch().ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.Augmentations.Effects).Switch().WhenAnyPropertyChanged().ToUnit(),
                this.WhenAnyValue(x => x.YoloEaseProject!.DataSources.InputDirectories).Switch().ToUnit())
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
            .Where(x => x != null)
            .Subscribe(x =>
            {
                if (!string.IsNullOrEmpty(x.RemoteProject.Username) &&
                    !string.IsNullOrEmpty(x.RemoteProject.Password) &&
                    !string.IsNullOrEmpty(x.RemoteProject.ServerUrl))
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
                                Message = $"Error: {e.Message}",
                                Placement = NotificationPlacement.TopRight,
                            });
                        }
                    });
                }
            })
            .AddTo(Anchors);

        this.WhenAnyValue(x => x.ProjectId)
            .EnableIf(this.WhenAnyValue(x => x.YoloEaseProject.RemoteProject.CurrentUser).Select(x => x != null))
            .SubscribeAsync(async _ =>
            {
                try
                {
                    await YoloEaseProject.RemoteProject.Refresh();
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

        additionalFilesSource.Add(new RefFileInfo(@"_content/AntDesign/js/ant-design-blazor.js"));
        additionalFilesSource.Add(new RefFileInfo(@"_content/AntDesign/css/ant-design-blazor.css"));
        additionalFilesSource.Add(new RefFileInfo(@"css/bootstrap.css"));
        additionalFilesSource.Add(new RefFileInfo(@"css/app.css"));
        
        additionalFilesSource.Add(new RefFileInfo(@"https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css"));
        additionalFilesSource.Add(new RefFileInfo(@"_content/Blazor.Bootstrap/blazor.bootstrap.css"));
        additionalFilesSource.Add(new RefFileInfo(@"https://cdnjs.cloudflare.com/ajax/libs/Chart.js/4.0.1/chart.umd.js"));
        additionalFilesSource.Add(new RefFileInfo(@"https://cdnjs.cloudflare.com/ajax/libs/chartjs-plugin-datalabels/2.2.0/chartjs-plugin-datalabels.min.js"));
        additionalFilesSource.Add(new RefFileInfo(@"https://cdn.jsdelivr.net/npm/sortablejs@latest/Sortable.min.js"));
        additionalFilesSource.Add(new RefFileInfo(@"_content/Blazor.Bootstrap/blazor.bootstrap.js"));
        
        additionalFilesSource.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Controls/PoeShared.Blazor.Controls.bundle.scp.css"));
        additionalFilesSource.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Controls/assets/css/main-colors.css"));
        additionalFilesSource.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Controls/assets/css/main-style.css"));
        additionalFilesSource.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Controls/assets/css/main-ant-blazor.css"));
        additionalFilesSource.Add(new RefFileInfo(@"YoloEase.styles.css"));
        
        additionalFilesSource.Add(new RefFileInfo(@"assets/css/yoloease.css"));
        
        additionalFilesSource.Add(new RefFileInfo(@"assets/js/split.min.js"));
        additionalFilesSource.Add(new RefFileInfo(@"assets/js/main.js"));

        NewProjectCommand = CommandWrapper.Create(NewProjectCommandExecuted);
        SaveProjectCommand = CommandWrapper.Create(SaveProjectCommandExecuted);
        SaveAsProjectCommand = CommandWrapper.Create(SaveAsProjectCommandExecuted);
        LoadProjectCommand = CommandWrapper.Create<object>(LoadProjectCommandExecuted);
        ExitAppCommand = CommandWrapper.Create(ExitAppCommandExecuted);

        RecentProjects = LoadRecentProjects().ToSourceList();

        this.WhenAnyValue(x => x.YoloEaseProject)
            .DoWithPrevious(x =>
            {
                if (x == null)
                {
                    return;
                }

                Log.Info($"Disposing previous project instance: {x}");
                x.Dispose();
            })
            .Subscribe()
            .AddTo(Anchors);

        Binder.Attach(this).AddTo(Anchors);
    }

    public Type Type { get; } = typeof(MainWindowComponent);

    public string Title { get; [UsedImplicitly] private set; }

    public IAppArguments AppArguments { get; }
    
    public ISourceList<RecentProjectInfo> RecentProjects { get; }

    public IReadOnlyObservableCollection<IFileInfo> AdditionalFiles { get; }

    public IObservableList<TabItem> Tabs { get; }

    public YoloEaseProject? YoloEaseProject { get; private set; }

    public int ProjectId { get; set; }

    public AutomaticTrainer AutomaticTrainer { get; }
    
    public AugmentationsAccessor Augmentations { get; }

    public string? LoadedProjectShortPath { get; [UsedImplicitly] private set; }

    public DirectoryInfo StorageDirectory { get; [UsedImplicitly] private set; }

    public DirectoryInfo ProjectOutputDirectory { get; [UsedImplicitly] private set; }

    public DirectoryInfo ProjectsDirectory { get; }

    public bool IsAdvancedMode { get; set; }

    public string ActiveTabId { get; set; }

    public bool IsSelected { get; set; }

    public ICommandWrapper NewProjectCommand { get; }

    public ICommandWrapper SaveProjectCommand { get; }

    public ICommandWrapper SaveAsProjectCommand { get; }

    public ICommandWrapper LoadProjectCommand { get; }

    public ICommandWrapper ExitAppCommand { get; }

    public FileInfo? LoadedProjectFile { get; private set; }

    public GeneralProperties LoadedProject { get; private set; }

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
        
        
        var newConfig = new GeneralProperties();
        var loadedConfig = LoadedProject;
        if (loadedConfig != null)
        {
            newConfig.Username = loadedConfig.Username;
            newConfig.Password = loadedConfig.Password;
            newConfig.ServerUrl = loadedConfig.ServerUrl;
        }

        var selectedFile = saveProjectFileDialog.ShowDialog();
        if (selectedFile != null)
        {
            SaveProjectConfig(newConfig, selectedFile);
            LoadProjectConfig(selectedFile);
        }
    }

    private async Task SaveProjectCommandExecuted()
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

    private async Task SaveAsProjectCommandExecuted()
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
            LoadedProjectFile = selectedFile;
            await SaveProjectCommandExecuted();
        }
    }

    private async Task LoadProjectCommandExecuted(object arg)
    {
        uiScheduler.Schedule(() =>
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
                LoadProjectConfig(selectedFile);
            }
        });
    }

    private void ExitAppCommandExecuted()
    {
        Log.Info("Closing application");
        applicationAccessor.Exit();
    }

    private static DirectoryInfo? PrepareProjectDirectory(DirectoryInfo? storageDirectory, int? projectId, string apiUrl)
    {
        if (storageDirectory == null || string.IsNullOrEmpty(apiUrl))
        {
            return null;
        }

        var uri = new Uri(apiUrl);
        return new DirectoryInfo(Path.Combine(storageDirectory.FullName, $"{uri.Host}_project_{projectId}"));
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

    public async Task RemoveDataFolderDirectory(DirectoryInfo directoryInfo)
    {
        YoloEaseProject!.DataSources.InputDirectories.Remove(directoryInfo);
    }

    public async Task AddDataFolderDirectory()
    {
        uiScheduler.Schedule(() =>
        {
            var selectedDirectory = folderBrowserDialog.ShowDialog();
            if (selectedDirectory != null)
            {
                YoloEaseProject?.DataSources.InputDirectories.AddOrUpdate(selectedDirectory);
            }
        });
    }

    public Task OpenProject()
    {
        return YoloEaseProject!.RemoteProject.NavigateToProject(YoloEaseProject.RemoteProject.ProjectId);
    }

    public async Task OpenAppDirectory()
    {
        await ProcessUtils.OpenFolder(new FileInfo(appArguments.ApplicationExecutablePath).Directory);
    }

    public async Task OpenAppDataDirectory()
    {
        await ProcessUtils.OpenFolder(new DirectoryInfo(appArguments.AppDataDirectory));
    }

    public async Task OpenStorage()
    {
        await ProcessUtils.OpenFolder(StorageDirectory);
    }

    public async Task SelectModel()
    {
        uiScheduler.Schedule(() =>
        {
            openFileDialog.InitialFileName = YoloEaseProject.TrainingDataset.BaseModelPath;
            if (openFileDialog.ShowDialog() != null)
            {
                YoloEaseProject.TrainingDataset.BaseModelPath = openFileDialog.LastFile.FullName;
            }
        });
    }

   
    private void LoadProjectConfig(FileInfo file)
    {
        var configJson = File.ReadAllText(file.FullName);
        var config = configSerializer.Deserialize<GeneralProperties>(configJson);
        LoadProjectConfig(config);
        LoadedProjectFile = file;
        if (Directory.Exists(StorageDirectory.FullName) == false)
        {
            Directory.CreateDirectory(StorageDirectory.FullName);
        }
        AddRecentProject(new RecentProjectInfo()
        {
            FilePath  = file.FullName,
            AccessTime = DateTime.Now
        });
    }
    
    private void LoadProjectConfig(GeneralProperties config)
    {
        var project = projectFactory.Create();
        project.RemoteProject.Username = config.Username;
        project.RemoteProject.Password = config.Password;
        project.RemoteProject.ServerUrl = config.ServerUrl;
        project.RemoteProject.ProjectId = config.ProjectId;
        project.DataSources.InputDirectories.EditDiff(config.DataDirectoryPaths.EmptyIfNull().Where(x => !string.IsNullOrEmpty(x)).Select(x => new DirectoryInfo(x)));
        project.TrainingDataset.BaseModelPath = config.BaseModelPath;
        project.TrainingDataset.Epochs = config.TrainingEpochs;
        project.TrainingDataset.ModelSize = config.ModelSize;
        project.TrainingBatch.BatchPercentage = config.BatchPercentage;
        project.TrainingDataset.TrainAdditionalArguments = config.TrainAdditionalArguments;
        project.Predictions.ConfidenceThresholdPercentage = config.PredictConfidenceThresholdPercentage;
        project.Predictions.IoUThresholdPercentage = config.PredictIoUThresholdPercentage;
        project.Predictions.PredictAdditionalArguments = config.PredictAdditionalArguments;
        if (!string.IsNullOrEmpty(config.AutoAnnotationModelPath))
        {
            project.Predictions.LoadModel(new FileInfo(config.AutoAnnotationModelPath));
        }

        AutomaticTrainer.ModelStrategy = config.AutoAnnotateModelStrategy;
        AutomaticTrainer.AutoAnnotate = config.AutoAnnotationIsEnabled;
        AutomaticTrainer.AutoAnnotateConfidenceThresholdPercentage = config.AutoAnnotateConfidenceThresholdPercentage;

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

        YoloEaseProject = project;
        LoadedProject = config;
    }

    private GeneralProperties PrepareProjectConfig()
    {
        var baseConfig = LoadedProject ?? new GeneralProperties();
        var updatedConfig = baseConfig with
        {
            Username = YoloEaseProject!.RemoteProject.Username,
            Password = YoloEaseProject.RemoteProject.Password,
            ServerUrl = YoloEaseProject.RemoteProject.ServerUrl,
            ProjectId = YoloEaseProject.RemoteProject.ProjectId,
            DataDirectoryPaths = YoloEaseProject.DataSources.InputDirectories.Items.Select(x => x.FullName).ToArray(),
            TrainingEpochs = YoloEaseProject.TrainingDataset.Epochs,
            ModelSize = YoloEaseProject.TrainingDataset.ModelSize,
            TrainValSplitPercentage = YoloEaseProject.TrainingDataset.TrainValSplitPercentage,
            BatchPercentage = YoloEaseProject.TrainingBatch.BatchPercentage,
            BaseModelPath = YoloEaseProject.TrainingDataset.BaseModelPath,
            TrainAdditionalArguments = YoloEaseProject.TrainingDataset.TrainAdditionalArguments,
            AutoAnnotationIsEnabled = AutomaticTrainer.AutoAnnotate,
            AutoAnnotationModelPath = YoloEaseProject.Predictions.PredictionModel?.ModelFile?.FullName,
            AutoAnnotateConfidenceThresholdPercentage = AutomaticTrainer.AutoAnnotateConfidenceThresholdPercentage,
            AutoAnnotateModelStrategy = AutomaticTrainer.ModelStrategy,

            PredictConfidenceThresholdPercentage = YoloEaseProject.Predictions.ConfidenceThresholdPercentage,
            PredictAdditionalArguments = YoloEaseProject.Predictions.PredictAdditionalArguments,
            
            Augmentations = SaveEffects(YoloEaseProject.Augmentations.Effects.Items)
        };
        return updatedConfig;
    }

    private static List<PoeConfigMetadata<IPoeEyeConfigVersioned>>? SaveEffects(IEnumerable<IImageEffect> effects)
    {
        return effects
            .Select(x => new PoeConfigMetadata<IPoeEyeConfigVersioned>(x.Properties))
            .ToList();
    }

    private void SaveProjectConfig(GeneralProperties config, FileInfo file)
    {
        if (file.Directory is {Exists: false})
        {
            file.Directory.Create();
        }
        var configJson = configSerializer.Serialize(config);
        File.WriteAllText(file.FullName, configJson);
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