using System.ComponentModel;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PoeShared.Modularity;
using PoeShared.Services;
using Shouldly;
using YoloEase.UI.Core;

namespace YoloEase.Tests.UI.Core;

public class AnnotationProjectAccessorFixture
{
    [Test]
    public async Task ShouldRecoverOfflineTasksAndLabelsFromAnnotationXml()
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
        instance.ProjectId = 207;

        await instance.Refresh().WaitAsync(TimeSpan.FromSeconds(5));

        var task = instance.Tasks.Items.ShouldHaveSingleItem();
        task.Id.ShouldBe(1286);
        task.Name.ShouldBe("Recovered task");
        task.Status.ShouldBe(AnnotationTaskStatus.Completed);

        instance.Labels.Items.Select(x => x.Name).OrderBy(x => x).ShouldBe(new[] { "Bomb", "Flower" });
        instance.ProjectFiles.Items.Select(x => x.FileName).OrderBy(x => x).ShouldBe(new[] { "frame-a.png", "frame-b.png" });
        File.Exists(Path.Combine(storageDirectory.FullName, "annotation", "tasks", "1286", "task.json")).ShouldBeTrue();
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
            ProjectName = "Offline",
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
