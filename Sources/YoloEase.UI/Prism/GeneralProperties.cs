using Newtonsoft.Json;
using PoeShared.Converters;
using YoloEase.UI.Core;
using YoloEase.UI.Augmentations;
using YoloEase.UI.TaskAnnotation;
using YoloEase.UI.TrainingTimeline;
namespace YoloEase.UI.Prism;

/// <summary>
/// Stores persisted general application settings that are shared outside a project file.
/// </summary>
public sealed record GeneralProperties : GeneralPropertiesV2;

public record GeneralPropertiesV2 : GeneralPropertiesV0
{
    public string StorageProjectSubfolder { get; set; } = string.Empty;

    public override int Version { get; set; } = 2;
}

public record GeneralPropertiesV0 : IPoeEyeConfigVersioned
{
    public string Username { get; set; } = string.Empty;

    [JsonConverter(typeof(SafeDataConverter))] public string Password { get; set; } = string.Empty;

    public string ServerUrl { get; set; } = "https://cvat.eyeauras.net";

    public AnnotationBackendMode AnnotationBackendMode { get; set; } = AnnotationBackendMode.Offline;

    public string ProjectName { get; set; } = string.Empty;

    public string[] DataDirectoryPaths { get; set; } = Array.Empty<string>();

    public string BaseModelPath { get; set; } = "yolov8s.pt";

    public string TrainAdditionalArguments { get; set; } = string.Empty;

    public int MaxNumberOfCpuCores { get; set; }

    public string PredictAdditionalArguments { get; set; } = string.Empty;

    public AutomaticTrainerModelStrategy PredictionModelStrategy { get; set; } = AutomaticTrainerModelStrategy.Latest;

    public string PredictionModelPath { get; set; } = string.Empty;

    public int ProjectId { get; set; }

    public int TrainingEpochs { get; set; } = 50;

    public int ModelSize { get; set; } = 640;

    public float TrainValSplitPercentage { get; set; } = 80;

    public bool AutoAnnotationIsEnabled { get; set; }

    public AutomaticTrainerModelStrategy AutoAnnotateModelStrategy { get; set; } = AutomaticTrainerModelStrategy.Latest;

    public string AutoAnnotationModelPath { get; set; } = string.Empty;

    public float AutoAnnotateConfidenceThresholdPercentage { get; set; } = 25;

    public float PredictConfidenceThresholdPercentage { get; set; } = 25;

    public float PredictIoUThresholdPercentage { get; set; } = 70;

    public List<AutoAnnotationModelProperties>? AutoAnnotationModels { get; set; }

    public int BatchPercentage { get; set; } = 5;

    public List<PoeConfigMetadata<IPoeEyeConfigVersioned>>? Augmentations { get; set; }

    public virtual int Version { get; set; }
}
