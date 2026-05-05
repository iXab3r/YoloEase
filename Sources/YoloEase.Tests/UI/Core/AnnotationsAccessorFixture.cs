using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PoeShared.Modularity;
using PoeShared.Scaffolding;
using PoeShared.Services;
using Shouldly;
using YoloEase.UI.Core;

namespace YoloEase.Tests.UI.Core;

/// <summary>
/// Verifies annotation-file discovery behavior for completed offline tasks.
/// </summary>
public class AnnotationsAccessorFixture
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// WHAT: Ensures completed tasks with missing local project files do not crash background trainer refresh.
    /// HOW: Recovers a completed offline task from XML while leaving the assets list empty, then refreshes annotations.
    /// </summary>
    [Test]
    public async Task ShouldCreateEmptyAnnotationRecordWhenCompletedTaskHasNoLocalFiles()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var serializer = new JsonTestConfigSerializer();
        var storageDirectory = new DirectoryInfo(Path.Combine(temp.Path, "storage"));
        var remoteProject = CreateRemoteProject(storageDirectory, serializer);
        await WriteAnnotationXml(storageDirectory, projectId: 207, taskId: 1286, fileNames: new[] { "missing-frame.png" });
        await remoteProject.Refresh().WaitAsync(Timeout);

        var instance = new AnnotationsAccessor(
            remoteProject,
            training: null!,
            new AnnotationsCache { StorageDirectory = storageDirectory },
            serializer,
            new EmptyAssetsAccessor());

        // When
        await instance.Refresh().WaitAsync(Timeout);

        // Then
        var annotation = instance.Annotations.Items.ShouldHaveSingleItem();
        annotation.TaskId.ShouldBe(1286);
        annotation.FilePath.ShouldBeNull();
        instance.AnnotatedTasks.Count.ShouldBe(1);
    }

    private static async Task WriteAnnotationXml(DirectoryInfo storageDirectory, int projectId, int taskId, IReadOnlyList<string> fileNames)
    {
        var trainingDirectory = new DirectoryInfo(Path.Combine(storageDirectory.FullName, "assets", "training"));
        trainingDirectory.Create();
        var annotationsFile = new FileInfo(Path.Combine(trainingDirectory.FullName, $"annotations.project.{projectId}.task.{taskId}.xml"));
        var images = string.Join(Environment.NewLine, fileNames.Select((fileName, index) => $"""
              <image id="{index}" name="{fileName}" width="320" height="320">
                <box label="Target" source="manual" xtl="1" ytl="2" xbr="3" ybr="4" />
              </image>
            """));

        await File.WriteAllTextAsync(annotationsFile.FullName, $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <annotations>
              <version>1.1</version>
              <meta>
                <task>
                  <id>{{taskId}}</id>
                  <name>Recovered task</name>
                  <created>2024-12-24 11:42:54.060462+00:00</created>
                  <updated>2024-12-24 11:48:05.249995+00:00</updated>
                  <labels>
                    <label>
                      <name>Target</name>
                      <color>#ddff33</color>
                    </label>
                  </labels>
                </task>
              </meta>
            {{images}}
            </annotations>
            """);
    }

    private static AnnotationProjectAccessor CreateRemoteProject(DirectoryInfo storageDirectory, IConfigSerializer serializer)
    {
        var cvatClient = new Mock<ICvatClient>();
        cvatClient.SetupAllProperties();

        var idGenerator = new Mock<IUniqueIdGenerator>();
        idGenerator.Setup(x => x.Next()).Returns("test");
        idGenerator.Setup(x => x.Next(It.IsAny<string>())).Returns((string prefix) => $"{prefix}test");

        return new AnnotationProjectAccessor(cvatClient.Object, idGenerator.Object, serializer)
        {
            Mode = AnnotationBackendMode.Offline,
            StorageDirectory = storageDirectory,
            ProjectId = 207,
            ProjectName = "Offline",
        };
    }

    private sealed class EmptyAssetsAccessor : IFileAssetsAccessor
    {
        private readonly SourceCacheEx<FileInfo, string> files = new(x => x.FullName);
        private readonly SourceCacheEx<DirectoryInfo, string> inputDirectories = new(x => x.FullName);

        public IObservableCacheEx<FileInfo, string> Files => files;

        public ISourceCacheEx<DirectoryInfo, string> InputDirectories => inputDirectories;
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
