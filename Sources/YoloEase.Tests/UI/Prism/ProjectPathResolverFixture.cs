using Shouldly;
using YoloEase.UI.Core;
using YoloEase.UI.Prism;
using YoloEase.UI.TaskAnnotation;
using YoloEase.UI.TrainingTimeline;

namespace YoloEase.Tests.UI.Prism;

public class ProjectPathResolverFixture
{
    [Test]
    public void ShouldDefaultGeneralPropertiesToOfflineBackend()
    {
        new GeneralProperties().AnnotationBackendMode.ShouldBe(AnnotationBackendMode.Offline);
    }

    [Test]
    public void ShouldConvertGeneralPropertiesV0ToCurrentV2Shape()
    {
        var v0 = new GeneralPropertiesV0
        {
            Username = "user",
            ProjectId = 73,
            ServerUrl = "https://cvat.eyeauras.net",
        };

        var v2 = new GeneralPropertiesConverterV0ToV2().Convert(v0);
        var current = new GeneralPropertiesConverter().Convert(v2);

        v2.Version.ShouldBe(2);
        current.Version.ShouldBe(2);
        current.Username.ShouldBe("user");
        current.ProjectId.ShouldBe(73);
    }

    [Test]
    public void ShouldMigrateV0CvatStoragePathsToPortableProjectRelativePaths()
    {
        using var temp = new TemporaryDirectory();
        var projectFile = new FileInfo(Path.Combine(temp.Path, "TL_Fishing.yeproj"));
        var config = new GeneralProperties
        {
            AnnotationBackendMode = AnnotationBackendMode.Cvat,
            ServerUrl = "https://cvat.eyeauras.net",
            ProjectId = 73,
            DataDirectoryPaths = new[]
            {
                @"C:\Users\Xab3r\AppData\Roaming\CVATAAT\release\projects\storage\cvat.eyeauras.net_project_73\local-sources\clip\0-100 x5",
                @"D:\CVATAAT\release\projects\storage\cvat.eyeauras.net_project_73\local-sources\other",
            },
            AutoAnnotationIsEnabled = true,
            AutoAnnotateModelStrategy = AutomaticTrainerModelStrategy.Custom,
            AutoAnnotationModelPath = @"D:\CVATAAT\release\projects\storage\cvat.eyeauras.net_project_73\datasets\train\runs\weights\model.onnx",
            AutoAnnotateConfidenceThresholdPercentage = 35,
        };

        var migrated = ProjectPathResolver.PrepareLoadedConfig(config, projectFile);

        migrated.Version.ShouldBe(2);
        migrated.StorageProjectSubfolder.ShouldBe("storage/cvat.eyeauras.net_project_73");
        migrated.DataDirectoryPaths.ShouldBe(new[]
        {
            "storage/cvat.eyeauras.net_project_73/local-sources/clip/0-100 x5",
            "storage/cvat.eyeauras.net_project_73/local-sources/other",
        });
        var migratedModel = migrated.AutoAnnotationModels.ShouldHaveSingleItem();
        migratedModel.SourceKind.ShouldBe(AutoAnnotationModelSourceKind.CustomOnnx);
        migratedModel.StorageRelativePath.ShouldBe("datasets/train/runs/weights/model.onnx");
        migratedModel.OriginalPath.ShouldBe(Path.Combine(temp.Path, "storage", "cvat.eyeauras.net_project_73", "datasets", "train", "runs", "weights", "model.onnx"));
    }

    [Test]
    public void ShouldSaveProjectOwnedPathsRelativeAndKeepExternalAbsolutePaths()
    {
        using var temp = new TemporaryDirectory();
        var projectFile = new FileInfo(Path.Combine(temp.Path, "Portable.yeproj"));
        var ownedSource = Path.Combine(temp.Path, "storage", "cvat.eyeauras.net_project_73", "local-sources", "owned");
        var externalSource = @"C:\external\source";
        var config = new GeneralProperties
        {
            AnnotationBackendMode = AnnotationBackendMode.Cvat,
            ServerUrl = "https://cvat.eyeauras.net",
            ProjectId = 73,
            StorageProjectSubfolder = "storage/cvat.eyeauras.net_project_73",
            DataDirectoryPaths = new[] { ownedSource, externalSource },
            AutoAnnotationModels = new List<AutoAnnotationModelProperties>
            {
                new()
                {
                    SourceKind = AutoAnnotationModelSourceKind.CustomOnnx,
                    OriginalPath = Path.Combine(temp.Path, "models", "custom.onnx"),
                    StorageRelativePath = @"models\auto-annotation\abc\custom.onnx",
                }
            }
        };

        var saved = ProjectPathResolver.PrepareConfigForSave(config, projectFile);

        saved.DataDirectoryPaths.ShouldBe(new[]
        {
            "storage/cvat.eyeauras.net_project_73/local-sources/owned",
            externalSource,
        });
        var model = saved.AutoAnnotationModels.ShouldHaveSingleItem();
        model.OriginalPath.ShouldBe("models/custom.onnx");
        model.StorageRelativePath.ShouldBe("models/auto-annotation/abc/custom.onnx");
    }

    [Test]
    public void ShouldResolveRelativeProjectPathsForLoad()
    {
        using var temp = new TemporaryDirectory();
        var projectFile = new FileInfo(Path.Combine(temp.Path, "Portable.yeproj"));

        var directory = ProjectPathResolver.ResolveDirectoryPathForLoad("storage/project/local-sources", projectFile);
        var modelPath = ProjectPathResolver.ResolveModelPathForLoad("models/base.pt", projectFile);
        var namedModel = ProjectPathResolver.ResolveModelPathForLoad("yolov8s.pt", projectFile);

        directory.FullName.ShouldBe(Path.Combine(temp.Path, "storage", "project", "local-sources"));
        modelPath.ShouldBe(Path.Combine(temp.Path, "models", "base.pt"));
        namedModel.ShouldBe("yolov8s.pt");
    }

    [Test]
    public void ShouldRejectAbsoluteAndEscapingStorageSubfolders()
    {
        ProjectPathResolver.TryNormalizeStorageProjectSubfolder(@"C:\temp", out _, out var absoluteError).ShouldBeFalse();
        absoluteError.ShouldContain("relative");

        ProjectPathResolver.TryNormalizeStorageProjectSubfolder("../outside", out _, out var escapingError).ShouldBeFalse();
        escapingError.ShouldContain("escape");

        ProjectPathResolver.TryNormalizeStorageProjectSubfolder("storage/project", out var normalized, out _).ShouldBeTrue();
        normalized.ShouldBe("storage/project");
    }

    [Test]
    public void ShouldDefaultNewOfflineProjectStorageToProjectFileName()
    {
        using var temp = new TemporaryDirectory();
        var projectFile = new FileInfo(Path.Combine(temp.Path, "My Project.yeproj"));
        var config = new GeneralProperties
        {
            AnnotationBackendMode = AnnotationBackendMode.Offline,
        };

        ProjectPathResolver.ResolveStorageProjectSubfolder(config, projectFile).ShouldBe("My Project");
        ProjectPathResolver.ResolveStorageDirectory(projectFile, "My Project")!.FullName.ShouldBe(Path.Combine(temp.Path, "My Project"));
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
