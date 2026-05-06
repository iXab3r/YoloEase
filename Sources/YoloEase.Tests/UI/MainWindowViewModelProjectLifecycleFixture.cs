using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PoeShared;
using PoeShared.Dialogs.Services;
using PoeShared.Modularity;
using PoeShared.Prism;
using PoeShared.Services;
using Shouldly;
using YoloEase.UI;
using YoloEase.UI.Augmentations;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;
using YoloEase.UI.Prerequisites;
using YoloEase.UI.Prism;
using YoloEase.UI.Services;
using YoloEase.UI.TaskAnnotation;
using YoloEase.UI.TrainingTimeline;
using YoloEase.UI.Yolo;

namespace YoloEase.Tests.UI;

/// <summary>
/// Verifies the main-window active-project sentinel lifecycle.
/// </summary>
[Apartment(ApartmentState.STA)]
public class MainWindowViewModelProjectLifecycleFixture
{
    /// <summary>
    /// WHAT: A fresh shell should expose an active project object, but that object must be the empty start-page sentinel.
    /// HOW: Creates the main window view model with a real project factory and inspects the initial active project.
    /// </summary>
    [Test]
    public void ShouldInitializeActiveProjectAsNonNullEmptyProject()
    {
        // Given / When
        using var temp = new TemporaryDirectory();
        using var context = CreateContext(temp);

        // Then
        context.ViewModel.YoloEaseProject.ShouldNotBeNull();
        context.ViewModel.YoloEaseProject.IsEmpty.ShouldBeTrue();
        context.ViewModel.ProjectId.ShouldBe(0);
        context.ViewModel.LoadedProjectFile.ShouldBeNull();
    }

    /// <summary>
    /// WHAT: Closing a loaded project should swap to a different empty active project and clear file/storage metadata.
    /// HOW: Loads a saved project, closes it through the command, then checks the active project and shell metadata.
    /// </summary>
    [Test]
    public async Task ShouldCloseProjectBySwappingToDifferentEmptyProjectAndClearingMetadata()
    {
        // Given
        using var temp = new TemporaryDirectory();
        using var context = CreateContext(temp);
        var projectFile = context.WriteProjectFile("project.yeproj", new GeneralProperties
        {
            ProjectId = 42,
            ProjectName = "Loaded project",
            StorageProjectSubfolder = "storage"
        });
        (await context.ViewModel.OpenProjectFile(projectFile)).ShouldBeTrue();
        var loadedProject = context.ViewModel.YoloEaseProject;
        loadedProject.IsEmpty.ShouldBeFalse();

        // When
        await context.ViewModel.CloseProjectCommand.ExecuteAsync();

        // Then
        context.ViewModel.YoloEaseProject.ShouldNotBeSameAs(loadedProject);
        context.ViewModel.YoloEaseProject.IsEmpty.ShouldBeTrue();
        context.ViewModel.LoadedProjectFile.ShouldBeNull();
        context.ViewModel.LoadedProject.ShouldBeNull();
        context.ViewModel.LoadedProjectShortPath.ShouldBeNull();
        context.ViewModel.StorageDirectory.ShouldBeNull();
        context.ViewModel.ProjectDirectory.ShouldBeNull();
        context.ViewModel.ProjectOutputDirectory.ShouldBeNull();
        context.ViewModel.StorageProjectSubfolder.ShouldBeNull();
        context.ViewModel.PendingStorageProjectSubfolder.ShouldBeNull();
        context.ViewModel.StorageProjectSubfolderError.ShouldBeNull();
        context.ViewModel.ProjectId.ShouldBe(0);
    }

    /// <summary>
    /// WHAT: Closing a loaded project should dispose the old non-empty project graph.
    /// HOW: Adds a sentinel disposable to the loaded project anchors and verifies it runs after close.
    /// </summary>
    [Test]
    public async Task ShouldDisposePreviousNonEmptyProjectOnClose()
    {
        // Given
        using var temp = new TemporaryDirectory();
        using var context = CreateContext(temp);
        var projectFile = context.WriteProjectFile("project.yeproj", new GeneralProperties
        {
            ProjectId = 7,
            ProjectName = "Disposable project",
            StorageProjectSubfolder = "storage"
        });
        (await context.ViewModel.OpenProjectFile(projectFile)).ShouldBeTrue();
        var loadedProject = context.ViewModel.YoloEaseProject;
        var disposed = false;
        loadedProject.Anchors.Add(Disposable.Create(() => disposed = true));

        // When
        await context.ViewModel.CloseProjectCommand.ExecuteAsync();

        // Then
        await WaitUntil(() => disposed);
        disposed.ShouldBeTrue();
        context.ViewModel.YoloEaseProject.ShouldNotBeSameAs(loadedProject);
    }

    /// <summary>
    /// WHAT: A failed load should preserve the previous active project object.
    /// HOW: Loads a valid project, attempts to load malformed project JSON, and checks the original project remains active.
    /// </summary>
    [Test]
    public async Task ShouldPreservePreviousActiveProjectWhenLoadFails()
    {
        // Given
        using var temp = new TemporaryDirectory();
        using var context = CreateContext(temp);
        var projectFile = context.WriteProjectFile("project.yeproj", new GeneralProperties
        {
            ProjectId = 11,
            ProjectName = "Stable project",
            StorageProjectSubfolder = "storage"
        });
        (await context.ViewModel.OpenProjectFile(projectFile)).ShouldBeTrue();
        var activeProject = context.ViewModel.YoloEaseProject;
        var invalidProjectFile = new FileInfo(Path.Combine(temp.Path, "invalid.yeproj"));
        await File.WriteAllTextAsync(invalidProjectFile.FullName, "{ this is not json");

        // When
        var loaded = await context.ViewModel.OpenProjectFile(invalidProjectFile);

        // Then
        loaded.ShouldBeFalse();
        context.ViewModel.YoloEaseProject.ShouldBeSameAs(activeProject);
        context.ViewModel.LoadedProjectFile!.FullName.ShouldBe(projectFile.FullName);
    }

    /// <summary>
    /// WHAT: The automatic trainer should ignore an empty active project.
    /// HOW: Assigns an empty project sentinel, then calls refresh/start and checks no timeline entries are added.
    /// </summary>
    [Test]
    public async Task ShouldIgnoreEmptyProjectInTrainer()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var factory = new TestProjectFactory(temp);
        var trainer = CreateTrainer();
        trainer.Project = factory.CreateEmptyProject();

        // When
        await trainer.Refresh();
        await trainer.Start();

        // Then
        trainer.Timeline.Count.ShouldBe(0);
        trainer.IsBusy.ShouldBeFalse();
    }

    private static TestContext CreateContext(TemporaryDirectory temp)
    {
        _ = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        Directory.CreateDirectory(Path.Combine(temp.Path, "app-data"));
        var serializer = new JsonTestConfigSerializer();
        var projectFactory = new TestProjectFactory(temp);
        var viewModel = new MainWindowViewModel(
            CreateOpenFileDialog().Object,
            CreateOpenFileDialog().Object,
            CreateSaveFileDialog().Object,
            CreateAppArguments(temp).Object,
            CreateTrainer(),
            CreatePrerequisites(temp),
            serializer,
            CreateApplicationAccessor().Object,
            projectFactory,
            ImmediateScheduler.Instance);
        return new TestContext(viewModel, serializer, projectFactory, temp);
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for lifecycle condition");
            }

            await Task.Delay(25);
        }
    }

    private static AutomaticTrainer CreateTrainer()
    {
        var clock = new Mock<IClock>();
        clock.Setup(x => x.Now).Returns(DateTime.Now);
        clock.Setup(x => x.UtcNow).Returns(DateTime.UtcNow);

        return new AutomaticTrainer(
            clock.Object,
            Mock.Of<IFactory<UpdateYoloTimelineEntry, TimelineController>>(),
            Mock.Of<IFactory<PrepareForCloudTrainingTimelineEntry, TimelineController, DatasetInfo>>(),
            Mock.Of<IFactory<PreTrainingTimelineEntry, TimelineController, DatasetInfo, Yolo8DatasetAccessor>>(),
            Mock.Of<IFactory<TrainingTimelineEntry, TimelineController, DatasetInfo, Yolo8DatasetAccessor, Yolo8PredictAccessor>>(),
            Mock.Of<IFactory<PredictTimelineEntry, TimelineController, PredictArgs, Yolo8DatasetAccessor, Yolo8PredictAccessor>>());
    }

    private static PrerequisitesViewModel CreatePrerequisites(TemporaryDirectory temp)
    {
        var toolchain = new Mock<IPrerequisitesToolchain>();
        toolchain.Setup(x => x.ToolsRoot).Returns(new DirectoryInfo(Path.Combine(temp.Path, "tools")));
        return new PrerequisitesViewModel(
            new TestConfigProvider(new YoloEaseApplicationConfig { CheckPrerequisitesAtStartup = false }),
            toolchain.Object,
            new CheckSuite());
    }

    private static Mock<IAppArguments> CreateAppArguments(TemporaryDirectory temp)
    {
        var appArguments = new Mock<IAppArguments>();
        appArguments.Setup(x => x.Version).Returns(new Version(1, 0, 0));
        appArguments.Setup(x => x.AppDataDirectory).Returns(Path.Combine(temp.Path, "app-data"));
        appArguments.Setup(x => x.ApplicationExecutablePath).Returns(Path.Combine(temp.Path, "YoloEase.exe"));
        return appArguments;
    }

    private static Mock<IOpenFileDialog> CreateOpenFileDialog()
    {
        var dialog = new Mock<IOpenFileDialog>();
        dialog.SetupAllProperties();
        dialog.Setup(x => x.ShowDialog()).Returns((FileInfo)null!);
        dialog.Setup(x => x.ShowDialogMultiselect()).Returns(ImmutableArray<FileInfo>.Empty);
        return dialog;
    }

    private static Mock<ISaveFileDialog> CreateSaveFileDialog()
    {
        var dialog = new Mock<ISaveFileDialog>();
        dialog.SetupAllProperties();
        dialog.Setup(x => x.ShowDialog()).Returns((FileInfo)null!);
        return dialog;
    }

    private static Mock<IApplicationAccessor> CreateApplicationAccessor()
    {
        var applicationAccessor = new Mock<IApplicationAccessor>();
        applicationAccessor.Setup(x => x.WhenExit).Returns(Observable.Empty<int>());
        applicationAccessor.Setup(x => x.WhenTerminate).Returns(Observable.Empty<int>());
        applicationAccessor.Setup(x => x.WhenLoaded).Returns(Observable.Empty<System.Reactive.Unit>());
        return applicationAccessor;
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(
            MainWindowViewModel viewModel,
            IConfigSerializer serializer,
            TestProjectFactory projectFactory,
            TemporaryDirectory temp)
        {
            ViewModel = viewModel;
            Serializer = serializer;
            ProjectFactory = projectFactory;
            Temp = temp;
        }

        public MainWindowViewModel ViewModel { get; }

        public IConfigSerializer Serializer { get; }

        public TestProjectFactory ProjectFactory { get; }

        public TemporaryDirectory Temp { get; }

        public FileInfo WriteProjectFile(string fileName, GeneralProperties config)
        {
            var file = new FileInfo(Path.Combine(Temp.Path, fileName));
            File.WriteAllText(file.FullName, Serializer.Serialize(config));
            return file;
        }

        public void Dispose()
        {
            ViewModel.Dispose();
        }
    }

    private sealed class TestProjectFactory : IFactory<YoloEaseProject>
    {
        private readonly TemporaryDirectory temp;
        private readonly IConfigSerializer serializer = new JsonTestConfigSerializer();
        private int id;

        public TestProjectFactory(TemporaryDirectory temp)
        {
            this.temp = temp;
        }

        public IReadOnlyList<YoloEaseProject> CreatedProjects => createdProjects;

        private List<YoloEaseProject> createdProjects { get; } = new();

        public YoloEaseProject Create()
        {
            var project = CreateProject();
            createdProjects.Add(project);
            return project;
        }

        public YoloEaseProject CreateEmptyProject()
        {
            var project = Create();
            project.IsEmpty = true;
            return project;
        }

        private YoloEaseProject CreateProject()
        {
            var idGenerator = new Mock<IUniqueIdGenerator>();
            idGenerator.Setup(x => x.Next()).Returns(() => Interlocked.Increment(ref id).ToString());
            idGenerator.Setup(x => x.Next(It.IsAny<string>())).Returns((string prefix) => $"{prefix}{Interlocked.Increment(ref id)}");

            var cvatClient = new Mock<ICvatClient>();
            cvatClient.SetupAllProperties();

            var yoloModelCachingService = new Mock<IYoloModelCachingService>();
            yoloModelCachingService.SetupAllProperties();
            yoloModelCachingService.Setup(x => x.CacheDirectory).Returns(new DirectoryInfo(Path.Combine(temp.Path, "model-cache")));

            var openFileDialog = CreateOpenFileDialog();
            var cliWrapper = new Yolo8CliWrapper(Mock.Of<IPrerequisitesToolchain>(), Mock.Of<IGpuRuntimeDetector>());
            var engineRepository = new Mock<IYoloEngineRepository>();
            engineRepository.Setup(x => x.ErrorProvider).Returns(new ErrorsProvider<object>(new object(), capacity: 10));

            var remoteProject = new AnnotationProjectAccessor(cvatClient.Object, idGenerator.Object, serializer)
            {
                Mode = AnnotationBackendMode.Offline,
                ProjectName = "Test project"
            };

            var dataSourcesFactory = new Mock<IFactory<DataSourcesProvider>>();
            dataSourcesFactory.Setup(x => x.Create()).Returns(() => new DataSourcesProvider());

            var predictFactory = new Mock<IFactory<Yolo8PredictAccessor>>();
            predictFactory
                .Setup(x => x.Create())
                .Returns(() => new Yolo8PredictAccessor(cliWrapper, openFileDialog.Object, ImmediateScheduler.Instance));

            var autoAnnotationFactory = new Mock<IFactory<AutoAnnotationAccessor>>();
            autoAnnotationFactory
                .Setup(x => x.Create())
                .Returns(() => new AutoAnnotationAccessor(engineRepository.Object, openFileDialog.Object, ImmediateScheduler.Instance));

            var augmentationsFactory = new Mock<IFactory<AugmentationsAccessor, AnnotationsAccessor>>();
            augmentationsFactory
                .Setup(x => x.Create(It.IsAny<AnnotationsAccessor>()))
                .Returns((AnnotationsAccessor annotations) => new AugmentationsAccessor(idGenerator.Object, annotations));

            var localStorageFactory = new Mock<IFactory<LocalStorageAssetsAccessor, DataSourcesProvider>>();
            localStorageFactory
                .Setup(x => x.Create(It.IsAny<DataSourcesProvider>()))
                .Returns((DataSourcesProvider dataSources) => new LocalStorageAssetsAccessor(dataSources));

            var trainingDatasetFactory = new Mock<IFactory<Yolo8DatasetAccessor, IFileAssetsAccessor>>();
            trainingDatasetFactory
                .Setup(x => x.Create(It.IsAny<IFileAssetsAccessor>()))
                .Returns((IFileAssetsAccessor assets) => new Yolo8DatasetAccessor(
                    serializer,
                    cliWrapper,
                    idGenerator.Object,
                    openFileDialog.Object,
                    assets,
                    yoloModelCachingService.Object,
                    ImmediateScheduler.Instance));

            var annotationsFactory = new Mock<IFactory<AnnotationsAccessor, AnnotationProjectAccessor, IFileAssetsAccessor, Yolo8DatasetAccessor>>();
            annotationsFactory
                .Setup(x => x.Create(It.IsAny<AnnotationProjectAccessor>(), It.IsAny<IFileAssetsAccessor>(), It.IsAny<Yolo8DatasetAccessor>()))
                .Returns((AnnotationProjectAccessor project, IFileAssetsAccessor assets, Yolo8DatasetAccessor training) => new AnnotationsAccessor(
                    project,
                    training,
                    new AnnotationsCache(),
                    serializer,
                    assets));

            var batchFactory = new Mock<IFactory<TrainingBatchAccessor, AnnotationProjectAccessor, IFileAssetsAccessor>>();
            batchFactory
                .Setup(x => x.Create(It.IsAny<AnnotationProjectAccessor>(), It.IsAny<IFileAssetsAccessor>()))
                .Returns((AnnotationProjectAccessor project, IFileAssetsAccessor assets) => new TrainingBatchAccessor(project, assets));

            return new YoloEaseProject(
                remoteProject,
                yoloModelCachingService.Object,
                dataSourcesFactory.Object,
                predictFactory.Object,
                autoAnnotationFactory.Object,
                augmentationsFactory.Object,
                localStorageFactory.Object,
                trainingDatasetFactory.Object,
                annotationsFactory.Object,
                batchFactory.Object);
        }
    }

    private sealed class JsonTestConfigSerializer : IConfigSerializer
    {
        private readonly JsonSerializerSettings settings = new()
        {
            ContractResolver = new DefaultContractResolver(),
        };

        public IDisposable DisablePooling()
        {
            return Disposable.Empty;
        }

        public void RegisterConverter(JsonConverter converter)
        {
            settings.Converters.Add(converter);
        }

        public string Serialize(object data)
        {
            return JsonConvert.SerializeObject(data, settings);
        }

        public void Serialize(object data, TextWriter textWriter)
        {
            textWriter.Write(Serialize(data));
        }

        public void Serialize(object data, FileInfo file)
        {
            File.WriteAllText(file.FullName, Serialize(data));
        }

        public object Deserialize(string serializedData, Type type)
        {
            return JsonConvert.DeserializeObject(serializedData, type, settings)!;
        }

        public object Deserialize(TextReader textReader, Type type)
        {
            return Deserialize(textReader.ReadToEnd(), type);
        }

        public object Deserialize(FileInfo file, Type type)
        {
            return Deserialize(File.ReadAllText(file.FullName), type);
        }

        public T Deserialize<T>(string serializedData)
        {
            return JsonConvert.DeserializeObject<T>(serializedData, settings)!;
        }

        public T Deserialize<T>(TextReader textReader)
        {
            return Deserialize<T>(textReader.ReadToEnd());
        }

        public T Deserialize<T>(FileInfo file)
        {
            return Deserialize<T>(File.ReadAllText(file.FullName));
        }

        public T DeserializeOrDefault<T>(PoeConfigMetadata<T> metadata, Func<PoeConfigMetadata<T>, T> defaultItemFactory) where T : class
        {
            return defaultItemFactory(metadata);
        }

        public T[] DeserializeSingleOrList<T>(string serializedData)
        {
            return JsonConvert.DeserializeObject<T[]>(serializedData, settings) ?? Array.Empty<T>();
        }

        public string Compress(object data)
        {
            return Serialize(data);
        }

        public T Decompress<T>(string compressedData)
        {
            return Deserialize<T>(compressedData);
        }
    }

    private sealed class TestConfigProvider : IConfigProvider<YoloEaseApplicationConfig>, IDisposable
    {
        private readonly BehaviorSubject<YoloEaseApplicationConfig> whenChanged;

        public TestConfigProvider(YoloEaseApplicationConfig config)
        {
            ActualConfig = config;
            whenChanged = new BehaviorSubject<YoloEaseApplicationConfig>(config);
        }

        public YoloEaseApplicationConfig ActualConfig { get; private set; }

        public IObservable<YoloEaseApplicationConfig> WhenChanged => whenChanged;

        public void Save(YoloEaseApplicationConfig config)
        {
            ActualConfig = config;
            whenChanged.OnNext(config);
        }

        public IObservable<T> ListenTo<T>(Expression<Func<YoloEaseApplicationConfig, T>> fieldToMonitor)
        {
            var getter = fieldToMonitor.Compile();
            return whenChanged.Select(getter).StartWith(getter(ActualConfig));
        }

        public void Dispose()
        {
            whenChanged.Dispose();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "YoloEaseLifecycleTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
