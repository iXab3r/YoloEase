using System.Linq;
using Newtonsoft.Json;

namespace YoloEase.UI.TaskAnnotation;

public enum AutoAnnotationModelSourceKind
{
    Latest = 0,
    CustomOnnx = 1,
}

public enum AutoAnnotationModelStatus
{
    NotChecked = 0,
    Ready = 1,
    NeedsMapping = 2,
    MissingFile = 3,
    UnsupportedModel = 4,
    LoadFailed = 5,
    LastRunFailed = 6,
    Running = 7,
}

public sealed class AutoAnnotationModelConfig : DisposableReactiveObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public int Order { get; set; }

    public string DisplayName { get; set; } = "Latest";

    public AutoAnnotationModelSourceKind SourceKind { get; set; } = AutoAnnotationModelSourceKind.Latest;

    public string? StorageRelativePath { get; set; }

    public string? OriginalPath { get; set; }

    public string? OriginalFileName { get; set; }

    public string? ContentSha256 { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool CreateSuggestions { get; set; }

    public float ConfidenceThresholdPercentage { get; set; } = 25;

    public float IoUThresholdPercentage { get; set; } = 70;

    public SourceListEx<AutoAnnotationLabelMapping> LabelMappings { get; } = new();

    [JsonIgnore]
    public AutoAnnotationModelStatus LastStatus { get; set; } = AutoAnnotationModelStatus.NotChecked;

    [JsonIgnore]
    public string? LastError { get; set; }

    [JsonIgnore]
    public string? LastResolvedModelPath { get; set; }

    [JsonIgnore]
    public string? LastResolvedModelHash { get; set; }

    [JsonIgnore]
    public long? LastResolvedModelLength { get; set; }

    [JsonIgnore]
    public DateTime? LastResolvedModelLastWriteTimeUtc { get; set; }

    [JsonIgnore]
    public DateTimeOffset? LastRunAt { get; set; }

    [JsonIgnore]
    public string? LastRunSummary { get; set; }

    public static AutoAnnotationModelConfig CreateLatest(string? displayName = null)
    {
        return new AutoAnnotationModelConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Latest" : displayName,
            SourceKind = AutoAnnotationModelSourceKind.Latest,
        };
    }

    public static AutoAnnotationModelConfig CreateCustom(string storageRelativePath, FileInfo originalFile)
    {
        return new AutoAnnotationModelConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = Path.GetFileNameWithoutExtension(originalFile.Name),
            SourceKind = AutoAnnotationModelSourceKind.CustomOnnx,
            StorageRelativePath = storageRelativePath,
            OriginalPath = originalFile.FullName,
            OriginalFileName = originalFile.Name,
        };
    }

    public AutoAnnotationModelConfig CloneAsNewEntry()
    {
        var result = new AutoAnnotationModelConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = $"{DisplayName} copy",
            SourceKind = SourceKind,
            StorageRelativePath = StorageRelativePath,
            OriginalPath = OriginalPath,
            OriginalFileName = OriginalFileName,
            ContentSha256 = ContentSha256,
            IsEnabled = IsEnabled,
            CreateSuggestions = CreateSuggestions,
            ConfidenceThresholdPercentage = ConfidenceThresholdPercentage,
            IoUThresholdPercentage = IoUThresholdPercentage,
        };
        result.LabelMappings.AddRange(LabelMappings.Items.Select(x => x.Clone()));
        return result;
    }

    public AutoAnnotationModelProperties ToProperties()
    {
        return new AutoAnnotationModelProperties
        {
            Id = Id,
            Order = Order,
            DisplayName = DisplayName,
            SourceKind = SourceKind,
            StorageRelativePath = StorageRelativePath,
            OriginalPath = OriginalPath,
            OriginalFileName = OriginalFileName,
            ContentSha256 = ContentSha256,
            IsEnabled = IsEnabled,
            CreateSuggestions = CreateSuggestions,
            ConfidenceThresholdPercentage = ConfidenceThresholdPercentage,
            IoUThresholdPercentage = IoUThresholdPercentage,
            LabelMappings = LabelMappings.Items.Select(x => x.ToProperties()).ToList(),
        };
    }

    public static AutoAnnotationModelConfig FromProperties(AutoAnnotationModelProperties properties)
    {
        var result = new AutoAnnotationModelConfig
        {
            Id = string.IsNullOrWhiteSpace(properties.Id) ? Guid.NewGuid().ToString("N") : properties.Id,
            Order = properties.Order,
            DisplayName = string.IsNullOrWhiteSpace(properties.DisplayName) ? "Latest" : properties.DisplayName,
            SourceKind = properties.SourceKind,
            StorageRelativePath = properties.StorageRelativePath,
            OriginalPath = properties.OriginalPath,
            OriginalFileName = properties.OriginalFileName,
            ContentSha256 = properties.ContentSha256,
            IsEnabled = properties.IsEnabled,
            CreateSuggestions = properties.CreateSuggestions,
            ConfidenceThresholdPercentage = properties.ConfidenceThresholdPercentage <= 0 ? 25 : properties.ConfidenceThresholdPercentage,
            IoUThresholdPercentage = properties.IoUThresholdPercentage <= 0 ? 70 : properties.IoUThresholdPercentage,
        };
        result.LabelMappings.AddRange(properties.LabelMappings.EmptyIfNull().Select(AutoAnnotationLabelMapping.FromProperties));
        return result;
    }
}

public sealed class AutoAnnotationLabelMapping : DisposableReactiveObject
{
    public int ModelLabelIndex { get; set; }

    public string ModelLabelName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public int? ProjectLabelId { get; set; }

    public string? ProjectLabelName { get; set; }

    public AutoAnnotationLabelMapping Clone()
    {
        return new AutoAnnotationLabelMapping
        {
            ModelLabelIndex = ModelLabelIndex,
            ModelLabelName = ModelLabelName,
            IsEnabled = IsEnabled,
            ProjectLabelId = ProjectLabelId,
            ProjectLabelName = ProjectLabelName,
        };
    }

    public AutoAnnotationLabelMappingProperties ToProperties()
    {
        return new AutoAnnotationLabelMappingProperties
        {
            ModelLabelIndex = ModelLabelIndex,
            ModelLabelName = ModelLabelName,
            IsEnabled = IsEnabled,
            ProjectLabelId = ProjectLabelId,
            ProjectLabelName = ProjectLabelName,
        };
    }

    public static AutoAnnotationLabelMapping FromProperties(AutoAnnotationLabelMappingProperties properties)
    {
        return new AutoAnnotationLabelMapping
        {
            ModelLabelIndex = properties.ModelLabelIndex,
            ModelLabelName = properties.ModelLabelName ?? string.Empty,
            IsEnabled = properties.IsEnabled,
            ProjectLabelId = properties.ProjectLabelId,
            ProjectLabelName = properties.ProjectLabelName,
        };
    }
}

public sealed record AutoAnnotationModelProperties
{
    public string Id { get; init; } = string.Empty;

    public int Order { get; init; }

    public string DisplayName { get; init; } = "Latest";

    public AutoAnnotationModelSourceKind SourceKind { get; init; } = AutoAnnotationModelSourceKind.Latest;

    public string? StorageRelativePath { get; init; }

    public string? OriginalPath { get; init; }

    public string? OriginalFileName { get; init; }

    public string? ContentSha256 { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool CreateSuggestions { get; init; }

    public float ConfidenceThresholdPercentage { get; init; } = 25;

    public float IoUThresholdPercentage { get; init; } = 70;

    public List<AutoAnnotationLabelMappingProperties>? LabelMappings { get; init; }
}

public sealed record AutoAnnotationLabelMappingProperties
{
    public int ModelLabelIndex { get; init; }

    public string? ModelLabelName { get; init; }

    public bool IsEnabled { get; init; } = true;

    public int? ProjectLabelId { get; init; }

    public string? ProjectLabelName { get; init; }
}
