using System.ComponentModel;
using System.Reactive.Linq;
using System.Xml.Linq;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PoeShared.Modularity;
using PoeShared.Services;
using Shouldly;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.Tests.UI.Core;

public class AnnotationProjectAccessorFixture
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ShouldRecoverTasksAndLabelsFromAnnotationXml()
    {
        using var temp = new TemporaryDirectory();
        var storageDirectory = new DirectoryInfo(Path.Combine(temp.Path, "storage"));
        var trainingDirectory = new DirectoryInfo(Path.Combine(storageDirectory.FullName, "assets", "training"));
        trainingDirectory.Create();
        var annotationsFile = new FileInfo(Path.Combine(trainingDirectory.FullName, "annotations.project.207.task.1286.xml"));
        await File.WriteAllTextAsync(annotationsFile.FullName, """
            <?xml version="1.0" encoding="utf-8"?>
            <annotations>
              <version>1.1</version>
              <meta>
                <task>
                  <id>1286</id>
                  <name>Recovered task</name>
                  <created>2024-12-24 11:42:54.060462+00:00</created>
                  <updated>2024-12-24 11:48:05.249995+00:00</updated>
                  <labels>
                    <label>
                      <name>Flower</name>
                      <color>#ddff33</color>
                    </label>
                    <label>
                      <name>Bomb</name>
                      <color>#fa3253</color>
                    </label>
                  </labels>
                </task>
              </meta>
              <image id="0" name="frame-a.png" width="320" height="320">
                <box label="Flower" source="manual" xtl="1" ytl="2" xbr="3" ybr="4" />
              </image>
              <image id="1" name="frame-b.png" width="320" height="320">
                <box label="Bomb" source="manual" xtl="5" ytl="6" xbr="7" ybr="8" />
              </image>
            </annotations>
            """);

        var instance = CreateInstance(storageDirectory);

        await instance.Refresh().WaitAsync(Timeout);

        var task = instance.Tasks.Items.ShouldHaveSingleItem();
        task.Id.ShouldBe(1286);
        task.Name.ShouldBe("Recovered task");
        task.Status.ShouldBe(AnnotationTaskStatus.Completed);

        instance.Labels.Items.Select(x => x.Name).OrderBy(x => x).ShouldBe(new[] { "Bomb", "Flower" });
        instance.ProjectFiles.Items.Select(x => x.FileName).OrderBy(x => x).ShouldBe(new[] { "frame-a.png", "frame-b.png" });
        File.Exists(Path.Combine(storageDirectory.FullName, "annotation", "tasks", "1286", "task.json")).ShouldBeTrue();
        File.Exists(Path.Combine(storageDirectory.FullName, "annotation", "project.json")).ShouldBeFalse();

        var annotations = await instance.RetrieveTaskAnnotations(task.Id);
        annotations.Count.ShouldBe(2);
        File.Exists(Path.Combine(storageDirectory.FullName, "annotation", "project.json")).ShouldBeFalse();
    }

    /// <summary>
    /// WHAT: Project identity and next ids are derived from workspace files instead of a project.json sidecar.
    /// HOW: Creates labels, tasks, and annotations, then verifies the former state file is never written.
    /// </summary>
    [Test]
    public async Task ShouldNotCreateProjectStateFileForProjectWorkspace()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var storageDirectory = new DirectoryInfo(Path.Combine(temp.Path, "storage"));
        var imageFile = new FileInfo(Path.Combine(temp.Path, "frame-a.png"));
        await File.WriteAllTextAsync(imageFile.FullName, string.Empty);
        var instance = CreateInstance(storageDirectory);
        var projectStateFile = Path.Combine(storageDirectory.FullName, "annotation", "project.json");

        // When
        await instance.Refresh().WaitAsync(Timeout);
        await instance.AddLabel("Flower");
        var task = await instance.CreateTask(new[] { imageFile });
        var label = instance.Labels.Items.ShouldHaveSingleItem();
        await instance.SaveTaskAnnotations(task.Id, new[]
        {
            new CvatRectangleAnnotation
            {
                Kind = CvatAnnotationShapeKind.Rectangle,
                FrameIndex = 0,
                LabelId = label.Id,
                BoundingBox = new RectangleD(1, 2, 3, 4),
                Source = "manual",
            },
        }, AnnotationTaskStatus.Completed);

        // Then
        File.Exists(projectStateFile).ShouldBeFalse();
        instance.Tasks.Items.ShouldHaveSingleItem().Id.ShouldBe(1);
        instance.Labels.Items.ShouldHaveSingleItem().Id.ShouldBe(1);
    }

    /// <summary>
    /// WHAT: Saving one task must not temporarily remove files that belong to another task from the shared task-file cache.
    /// HOW: Creates two tasks, records project-file cache snapshots while finishing the first task, and checks the second task file remains present.
    /// </summary>
    [Test]
    public async Task ShouldKeepOtherTaskFilesVisibleWhenSavingTask()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var storageDirectory = new DirectoryInfo(Path.Combine(temp.Path, "storage"));
        var firstFile = new FileInfo(Path.Combine(temp.Path, "frame-a.png"));
        var secondFile = new FileInfo(Path.Combine(temp.Path, "frame-b.png"));
        await File.WriteAllTextAsync(firstFile.FullName, string.Empty);
        await File.WriteAllTextAsync(secondFile.FullName, string.Empty);

        var instance = CreateInstance(storageDirectory);
        var firstTask = await instance.CreateTask(new[] { firstFile });
        var secondTask = await instance.CreateTask(new[] { secondFile });
        var secondTaskFileKey = $"{secondTask.Id}:{secondFile.Name}";
        var fileSnapshots = new List<string[]>();
        using var subscription = instance.ProjectFiles.Connect().Subscribe(_ =>
        {
            fileSnapshots.Add(instance.ProjectFiles.Items
                .Select(x => $"{x.TaskId}:{x.FileName}")
                .OrderBy(x => x)
                .ToArray());
        });
        fileSnapshots.Clear();

        // When
        await instance.SaveTaskAnnotations(firstTask.Id, Array.Empty<CvatRectangleAnnotation>(), AnnotationTaskStatus.Completed);

        // Then
        fileSnapshots.Any(x => !x.Contains(secondTaskFileKey)).ShouldBeFalse();
        instance.ProjectFiles.Items
            .Select(x => $"{x.TaskId}:{x.FileName}")
            .OrderBy(x => x)
            .ShouldBe(new[] { $"{firstTask.Id}:{firstFile.Name}", secondTaskFileKey });
        instance.Tasks.Items.Single(x => x.Id == firstTask.Id).Status.ShouldBe(AnnotationTaskStatus.Completed);
    }

    /// <summary>
    /// WHAT: Annotation reads, writes, and status updates for one project are serialized through one project queue.
    /// HOW: Runs saves, annotation reads, and status changes concurrently against the same task, then verifies the final XML and task state.
    /// </summary>
    [Test]
    public async Task ShouldSerializeConcurrentAnnotationOperations()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var storageDirectory = new DirectoryInfo(Path.Combine(temp.Path, "storage"));
        var instance = CreateInstance(storageDirectory);
        var setup = await CreateSingleFrameTask(instance, temp);

        // When
        var operations = Enumerable.Range(0, 45).Select(async index =>
        {
            switch (index % 3)
            {
                case 0:
                    await instance.SaveTaskAnnotations(setup.Task.Id, new[] { CreateBox(setup.Label.Id, index + 1) }, AnnotationTaskStatus.InProgress);
                    break;
                case 1:
                    _ = await instance.RetrieveTaskAnnotations(setup.Task.Id);
                    break;
                default:
                    await instance.UpdateTaskStatus(setup.Task.Id, index % 2 == 0 ? AnnotationTaskStatus.Completed : AnnotationTaskStatus.InProgress);
                    break;
            }
        });
        await Task.WhenAll(operations).WaitAsync(Timeout);

        await instance.SaveTaskAnnotations(setup.Task.Id, new[] { CreateBox(setup.Label.Id, 99) }, AnnotationTaskStatus.Completed).WaitAsync(Timeout);

        // Then
        var annotations = await instance.RetrieveTaskAnnotations(setup.Task.Id).WaitAsync(Timeout);
        annotations.ShouldHaveSingleItem().BoundingBox.X.ShouldBe(99);
        instance.Tasks.Items.Single(x => x.Id == setup.Task.Id).Status.ShouldBe(AnnotationTaskStatus.Completed);
        XDocument.Load(GetAnnotationsXmlFile(storageDirectory, setup.Task.Id).FullName)
            .Descendants("box")
            .Count()
            .ShouldBe(1);
    }

    /// <summary>
    /// WHAT: A short-lived external read handle on the annotation XML should not crash a save.
    /// HOW: Locks the existing XML with a read-only sharing mode, starts a save, releases the lock, and verifies the retry succeeds.
    /// </summary>
    [Test]
    public async Task ShouldRetryAnnotationSaveWhenXmlFileIsTemporarilyLocked()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var storageDirectory = new DirectoryInfo(Path.Combine(temp.Path, "storage"));
        var instance = CreateInstance(storageDirectory);
        var setup = await CreateSingleFrameTask(instance, temp);
        await instance.SaveTaskAnnotations(setup.Task.Id, new[] { CreateBox(setup.Label.Id, 1) }, AnnotationTaskStatus.InProgress).WaitAsync(Timeout);

        var annotationsFile = GetAnnotationsXmlFile(storageDirectory, setup.Task.Id);
        using var lockedStream = new FileStream(annotationsFile.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);

        // When
        var saveTask = instance.SaveTaskAnnotations(setup.Task.Id, new[] { CreateBox(setup.Label.Id, 42) }, AnnotationTaskStatus.Completed);
        await Task.Delay(300);
        lockedStream.Dispose();
        await saveTask.WaitAsync(Timeout);

        // Then
        var annotations = await instance.RetrieveTaskAnnotations(setup.Task.Id).WaitAsync(Timeout);
        annotations.ShouldHaveSingleItem().BoundingBox.X.ShouldBe(42);
        instance.Tasks.Items.Single(x => x.Id == setup.Task.Id).Status.ShouldBe(AnnotationTaskStatus.Completed);
    }

    /// <summary>
    /// WHAT: Rapid annotation saves should leave a complete CVAT XML file after each atomic replacement.
    /// HOW: Saves several annotation revisions and parses the XML from disk after every save.
    /// </summary>
    [Test]
    public async Task ShouldLeaveValidXmlAfterRapidAnnotationSaves()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var storageDirectory = new DirectoryInfo(Path.Combine(temp.Path, "storage"));
        var instance = CreateInstance(storageDirectory);
        var setup = await CreateSingleFrameTask(instance, temp);
        var annotationsFile = GetAnnotationsXmlFile(storageDirectory, setup.Task.Id);

        // When / Then
        for (var index = 0; index < 20; index++)
        {
            await instance.SaveTaskAnnotations(setup.Task.Id, new[] { CreateBox(setup.Label.Id, index + 1) }, AnnotationTaskStatus.InProgress).WaitAsync(Timeout);
            var document = XDocument.Load(annotationsFile.FullName);
            document.Root?.Name.LocalName.ShouldBe("annotations");
            document.Descendants("box").ShouldHaveSingleItem();
        }
    }

    private static async Task<(AnnotationTaskInfo Task, AnnotationLabelInfo Label)> CreateSingleFrameTask(AnnotationProjectAccessor instance, TemporaryDirectory temp)
    {
        var imageFile = new FileInfo(Path.Combine(temp.Path, "frame-a.png"));
        await File.WriteAllTextAsync(imageFile.FullName, string.Empty);
        await instance.Refresh().WaitAsync(Timeout);
        await instance.AddLabel("Flower").WaitAsync(Timeout);
        var task = await instance.CreateTask(new[] { imageFile }).WaitAsync(Timeout);
        var label = instance.Labels.Items.ShouldHaveSingleItem();
        return (task, label);
    }

    private static CvatRectangleAnnotation CreateBox(int labelId, double x)
    {
        return new CvatRectangleAnnotation
        {
            Kind = CvatAnnotationShapeKind.Rectangle,
            FrameIndex = 0,
            LabelId = labelId,
            BoundingBox = new RectangleD(x, 2, 3, 4),
            Source = "manual",
        };
    }

    private static FileInfo GetAnnotationsXmlFile(DirectoryInfo storageDirectory, int taskId)
    {
        return new FileInfo(Path.Combine(storageDirectory.FullName, "assets", "training", $"annotations.project.1.task.{taskId}.xml"));
    }

    private static AnnotationProjectAccessor CreateInstance(DirectoryInfo storageDirectory)
    {
        var cvatClient = new Mock<ICvatClient>();
        cvatClient.SetupAllProperties();
        cvatClient.As<INotifyPropertyChanged>();

        var idGenerator = new Mock<IUniqueIdGenerator>();
        idGenerator.Setup(x => x.Next()).Returns("test");
        idGenerator.Setup(x => x.Next(It.IsAny<string>())).Returns((string prefix) => $"{prefix}test");

        return new AnnotationProjectAccessor(cvatClient.Object, idGenerator.Object, new JsonTestConfigSerializer())
        {
            Mode = AnnotationBackendMode.Offline,
            StorageDirectory = storageDirectory,
            ProjectName = "Project",
        };
    }

    private sealed class JsonTestConfigSerializer : IConfigSerializer
    {
        private readonly JsonSerializerSettings settings = new()
        {
            ContractResolver = new DefaultContractResolver(),
        };

        public IDisposable DisablePooling()
        {
            return EmptyDisposable.Instance;
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

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "YoloEaseTests", Guid.NewGuid().ToString("N"));
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
