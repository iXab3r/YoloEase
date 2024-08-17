using Newtonsoft.Json;
using PoeShared.Converters;
using YoloEase.UI.Augmentations;
using YoloEase.UI.TrainingTimeline;

namespace YoloEase.UI.Prism;

public sealed record GeneralProperties : IPoeEyeConfigVersioned
{
    public string Username { get; set; }

    [JsonConverter(typeof(SafeDataConverter))] public string Password { get; set; }

    public string ServerUrl { get; set; } = "https://cvat.eyeauras.net";

    public string[] DataDirectoryPaths { get; set; }

    public string BaseModelPath { get; set; } = "yolov8s.pt";
    
    public string TrainAdditionalArguments { get; set; }
    
    public string PredictAdditionalArguments { get; set; }
    
    public int ProjectId { get; set; }
    
    public int TrainingEpochs { get; set; } = 50;
    
    public int ModelSize { get; set; } = 640;
    
    public bool AutoAnnotationIsEnabled { get; set; }
    
    public AutomaticTrainerModelStrategy AutoAnnotateModelStrategy { get; set; } = AutomaticTrainerModelStrategy.Latest;
    
    public string AutoAnnotationModelPath { get; set; }
    
    public float AutoAnnotateConfidenceThresholdPercentage { get; set; } = 25;
    
    public float PredictConfidenceThresholdPercentage { get; set; } = 25;
    
    public float PredictIoUThresholdPercentage { get; set; } = 70;
    
    public int BatchPercentage { get; set; } = 5;
    
    public List<PoeConfigMetadata<IPoeEyeConfigVersioned>>? Augmentations { get; set; } 

    public int Version { get; set; }
}