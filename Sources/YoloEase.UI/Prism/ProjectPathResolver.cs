using System.Linq;
using YoloEase.UI.Core;
using YoloEase.UI.TaskAnnotation;
using YoloEase.UI.TrainingTimeline;

namespace YoloEase.UI.Prism;

/// <summary>
/// Resolves project-owned paths relative to the .yeproj file and the persisted storage subfolder.
/// </summary>
internal static class ProjectPathResolver
{
    private static readonly char[] PortableSeparators = { '/', '\\' };

    public static GeneralProperties PrepareLoadedConfig(GeneralProperties config, FileInfo projectFile)
    {
        var storageSubfolder = ResolveStorageProjectSubfolder(config, projectFile);
        var legacyStorageSubfolder = ResolveLegacyStorageProjectSubfolder(config, projectFile);

        return config with
        {
            Version = 2,
            StorageProjectSubfolder = storageSubfolder,
            DataDirectoryPaths = config.DataDirectoryPaths
                .EmptyIfNull()
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => NormalizeProjectPathForSave(x, projectFile, storageSubfolder, legacyStorageSubfolder))
                .ToArray(),
            BaseModelPath = NormalizeModelPathForSave(config.BaseModelPath, projectFile, storageSubfolder, legacyStorageSubfolder),
            PredictionModelPath = NormalizeProjectPathForSave(config.PredictionModelPath, projectFile, storageSubfolder, legacyStorageSubfolder),
            AutoAnnotationModelPath = NormalizeProjectPathForSave(config.AutoAnnotationModelPath, projectFile, storageSubfolder, legacyStorageSubfolder),
            AutoAnnotationModels = PrepareAutoAnnotationModelsForLoad(config, projectFile, storageSubfolder, legacyStorageSubfolder),
        };
    }

    public static GeneralProperties PrepareConfigForSave(GeneralProperties config, FileInfo projectFile)
    {
        var storageSubfolder = ResolveStorageProjectSubfolder(config, projectFile);
        var legacyStorageSubfolder = ResolveLegacyStorageProjectSubfolder(config, projectFile);

        return config with
        {
            Version = 2,
            StorageProjectSubfolder = storageSubfolder,
            DataDirectoryPaths = config.DataDirectoryPaths
                .EmptyIfNull()
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => NormalizeProjectPathForSave(x, projectFile, storageSubfolder, legacyStorageSubfolder))
                .ToArray(),
            BaseModelPath = NormalizeModelPathForSave(config.BaseModelPath, projectFile, storageSubfolder, legacyStorageSubfolder),
            PredictionModelPath = NormalizeProjectPathForSave(config.PredictionModelPath, projectFile, storageSubfolder, legacyStorageSubfolder),
            AutoAnnotationModelPath = NormalizeProjectPathForSave(config.AutoAnnotationModelPath, projectFile, storageSubfolder, legacyStorageSubfolder),
            AutoAnnotationModels = NormalizeAutoAnnotationModelsForSave(config.AutoAnnotationModels, projectFile, storageSubfolder, legacyStorageSubfolder),
        };
    }

    public static DirectoryInfo? ResolveStorageDirectory(FileInfo? projectFile, string? storageProjectSubfolder)
    {
        if (projectFile?.Directory == null)
        {
            return null;
        }

        if (!TryNormalizeStorageProjectSubfolder(storageProjectSubfolder, out var normalized, out _))
        {
            return null;
        }

        return new DirectoryInfo(Path.GetFullPath(Path.Combine(projectFile.Directory.FullName, ToNativePath(normalized))));
    }

    public static string ResolveStorageProjectSubfolder(GeneralPropertiesV0 config, FileInfo projectFile)
    {
        if (config is GeneralPropertiesV2 v2 &&
            TryNormalizeStorageProjectSubfolder(v2.StorageProjectSubfolder, out var explicitSubfolder, out _))
        {
            return explicitSubfolder;
        }

        var fallback = ResolveDefaultStorageProjectSubfolder(config, projectFile);
        if (!TryNormalizeStorageProjectSubfolder(fallback, out var normalizedFallback, out var error))
        {
            throw new InvalidOperationException(error ?? $"Invalid storage subfolder: {fallback}");
        }

        return normalizedFallback;
    }

    public static string ResolveDefaultStorageProjectSubfolder(GeneralPropertiesV0 config, FileInfo projectFile)
    {
        if (config.AnnotationBackendMode == AnnotationBackendMode.Cvat && config.ProjectId > 0)
        {
            return $"storage/{ResolveLegacyStorageLeaf(config)}";
        }

        return SanitizePathSegment(Path.GetFileNameWithoutExtension(projectFile.Name));
    }

    public static bool TryNormalizeStorageProjectSubfolder(string? value, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Storage subfolder is required.";
            return false;
        }

        var trimmed = value.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            error = "Storage subfolder must be relative to the project file.";
            return false;
        }

        var parts = trimmed
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x != ".")
            .ToArray();
        if (parts.Length <= 0)
        {
            error = "Storage subfolder is required.";
            return false;
        }

        if (parts.Any(x => x == ".."))
        {
            error = "Storage subfolder must not escape the project folder.";
            return false;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var invalidSegment = parts.FirstOrDefault(x => x.IndexOfAny(invalidChars) >= 0);
        if (invalidSegment != null)
        {
            error = $"Storage subfolder contains invalid path segment: {invalidSegment}";
            return false;
        }

        normalized = string.Join("/", parts);
        return true;
    }

    public static DirectoryInfo ResolveDirectoryPathForLoad(string path, FileInfo projectFile)
    {
        return new DirectoryInfo(ResolveProjectPathForLoad(path, projectFile));
    }

    public static string ResolveModelPathForLoad(string? path, FileInfo projectFile)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var candidate = new FileInfo(Path.GetFullPath(Path.Combine(projectFile.DirectoryName ?? string.Empty, ToNativePath(path))));
        return IsRelativePathLike(path) || candidate.Exists ? candidate.FullName : path;
    }

    public static string ResolveProjectPathForLoad(string? path, FileInfo projectFile)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(projectFile.DirectoryName ?? string.Empty, ToNativePath(path)));
    }

    public static string NormalizeProjectPathForSave(string? path, FileInfo projectFile, string storageProjectSubfolder, string? legacyStorageSubfolder = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (!Path.IsPathRooted(path))
        {
            return NormalizePortablePath(path);
        }

        var storageAnchoredPath = TryMapAbsoluteStoragePathToProjectRelative(path, storageProjectSubfolder, legacyStorageSubfolder);
        if (!string.IsNullOrWhiteSpace(storageAnchoredPath))
        {
            return storageAnchoredPath;
        }

        if (TryMakeProjectRelative(path, projectFile, out var relativePath))
        {
            return relativePath;
        }

        return path;
    }

    public static string NormalizeModelPathForSave(string? path, FileInfo projectFile, string storageProjectSubfolder, string? legacyStorageSubfolder = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (!Path.IsPathRooted(path))
        {
            return NormalizePortablePath(path);
        }

        return NormalizeProjectPathForSave(path, projectFile, storageProjectSubfolder, legacyStorageSubfolder);
    }

    public static string NormalizeStorageRelativePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : NormalizePortablePath(path);
    }

    private static List<AutoAnnotationModelProperties>? PrepareAutoAnnotationModelsForLoad(
        GeneralProperties config,
        FileInfo projectFile,
        string storageProjectSubfolder,
        string? legacyStorageSubfolder)
    {
        var configuredModels = config.AutoAnnotationModels.EmptyIfNull().ToArray();
        if (configuredModels.Length > 0)
        {
            return configuredModels
                .Select(x => PrepareAutoAnnotationModelForLoad(x, projectFile, storageProjectSubfolder, legacyStorageSubfolder))
                .ToList();
        }

        if (!config.AutoAnnotationIsEnabled && string.IsNullOrWhiteSpace(config.AutoAnnotationModelPath))
        {
            return null;
        }

        var autoModelPath = config.AutoAnnotationModelPath;
        var normalizedAutoModelPath = NormalizeProjectPathForSave(autoModelPath, projectFile, storageProjectSubfolder, legacyStorageSubfolder);
        var storageRelativePath = TryResolveStorageRelativePath(autoModelPath, storageProjectSubfolder, legacyStorageSubfolder, out var resolvedStorageRelativePath)
            ? resolvedStorageRelativePath
            : null;

        var isLatest = config.AutoAnnotateModelStrategy == AutomaticTrainerModelStrategy.Latest;
        return new List<AutoAnnotationModelProperties>
        {
            new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Order = 0,
                DisplayName = isLatest
                    ? "Latest"
                    : Path.GetFileNameWithoutExtension(autoModelPath) ?? "Custom model",
                SourceKind = isLatest
                    ? AutoAnnotationModelSourceKind.Latest
                    : AutoAnnotationModelSourceKind.CustomOnnx,
                StorageRelativePath = isLatest ? null : storageRelativePath,
                OriginalPath = string.IsNullOrWhiteSpace(normalizedAutoModelPath) ? null : ResolveProjectPathForLoad(normalizedAutoModelPath, projectFile),
                OriginalFileName = string.IsNullOrWhiteSpace(autoModelPath) ? null : Path.GetFileName(autoModelPath),
                IsEnabled = config.AutoAnnotationIsEnabled,
                ConfidenceThresholdPercentage = config.AutoAnnotateConfidenceThresholdPercentage <= 0
                    ? 25
                    : config.AutoAnnotateConfidenceThresholdPercentage,
                IoUThresholdPercentage = config.PredictIoUThresholdPercentage <= 0 ? 70 : config.PredictIoUThresholdPercentage,
            }
        };
    }

    private static AutoAnnotationModelProperties PrepareAutoAnnotationModelForLoad(
        AutoAnnotationModelProperties model,
        FileInfo projectFile,
        string storageProjectSubfolder,
        string? legacyStorageSubfolder)
    {
        var storageRelativePath = NormalizeStorageRelativePath(model.StorageRelativePath);
        var normalizedOriginalPath = NormalizeProjectPathForSave(model.OriginalPath, projectFile, storageProjectSubfolder, legacyStorageSubfolder);
        var originalPath = string.IsNullOrWhiteSpace(normalizedOriginalPath)
            ? null
            : ResolveProjectPathForLoad(normalizedOriginalPath, projectFile);

        if (string.IsNullOrWhiteSpace(storageRelativePath) &&
            TryResolveStorageRelativePath(model.OriginalPath, storageProjectSubfolder, legacyStorageSubfolder, out var migratedStorageRelativePath))
        {
            storageRelativePath = migratedStorageRelativePath;
        }

        return model with
        {
            StorageRelativePath = string.IsNullOrWhiteSpace(storageRelativePath) ? null : storageRelativePath,
            OriginalPath = originalPath,
        };
    }

    private static List<AutoAnnotationModelProperties>? NormalizeAutoAnnotationModelsForSave(
        IEnumerable<AutoAnnotationModelProperties>? models,
        FileInfo projectFile,
        string storageProjectSubfolder,
        string? legacyStorageSubfolder)
    {
        return models
            .EmptyIfNull()
            .Select(x =>
            {
                var storageRelativePath = NormalizeStorageRelativePath(x.StorageRelativePath);
                if (string.IsNullOrWhiteSpace(storageRelativePath) &&
                    TryResolveStorageRelativePath(x.OriginalPath, storageProjectSubfolder, legacyStorageSubfolder, out var migratedStorageRelativePath))
                {
                    storageRelativePath = migratedStorageRelativePath;
                }

                return x with
                {
                    StorageRelativePath = string.IsNullOrWhiteSpace(storageRelativePath) ? null : storageRelativePath,
                    OriginalPath = string.IsNullOrWhiteSpace(x.OriginalPath)
                        ? null
                        : NormalizeProjectPathForSave(x.OriginalPath, projectFile, storageProjectSubfolder, legacyStorageSubfolder),
                };
            })
            .ToList();
    }

    private static bool TryResolveStorageRelativePath(
        string? path,
        string storageProjectSubfolder,
        string? legacyStorageSubfolder,
        out string storageRelativePath)
    {
        storageRelativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var portablePath = NormalizePortablePath(path);
        if (!Path.IsPathRooted(path))
        {
            var storagePrefix = NormalizePortablePath(storageProjectSubfolder).TrimEnd('/');
            if (portablePath.Equals(storagePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (portablePath.StartsWith($"{storagePrefix}/", StringComparison.OrdinalIgnoreCase))
            {
                storageRelativePath = portablePath[(storagePrefix.Length + 1)..];
                return !string.IsNullOrWhiteSpace(storageRelativePath);
            }

            return false;
        }

        foreach (var candidate in GetStorageSubfolderCandidates(storageProjectSubfolder, legacyStorageSubfolder))
        {
            if (TrySliceAfterSubpath(portablePath, candidate, out var suffix) && !string.IsNullOrWhiteSpace(suffix))
            {
                storageRelativePath = suffix;
                return true;
            }
        }

        return false;
    }

    private static string? TryMapAbsoluteStoragePathToProjectRelative(
        string path,
        string storageProjectSubfolder,
        string? legacyStorageSubfolder)
    {
        var portablePath = NormalizePortablePath(path);
        foreach (var candidate in GetStorageSubfolderCandidates(storageProjectSubfolder, legacyStorageSubfolder))
        {
            if (TrySliceAfterSubpath(portablePath, candidate, out var suffix))
            {
                return string.IsNullOrEmpty(suffix)
                    ? NormalizePortablePath(storageProjectSubfolder)
                    : $"{NormalizePortablePath(storageProjectSubfolder).TrimEnd('/')}/{suffix}";
            }
        }

        return null;
    }

    private static IEnumerable<string> GetStorageSubfolderCandidates(string storageProjectSubfolder, string? legacyStorageSubfolder)
    {
        var current = NormalizePortablePath(storageProjectSubfolder).Trim('/');
        if (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;
        }

        var legacy = NormalizePortablePath(legacyStorageSubfolder).Trim('/');
        if (!string.IsNullOrWhiteSpace(legacy) && !legacy.Equals(current, StringComparison.OrdinalIgnoreCase))
        {
            yield return legacy;
        }

        var legacyLeaf = GetLastPathSegment(legacy);
        if (!string.IsNullOrWhiteSpace(legacyLeaf) && !legacyLeaf.Equals(current, StringComparison.OrdinalIgnoreCase))
        {
            yield return legacyLeaf;
        }
    }

    private static bool TryMakeProjectRelative(string path, FileInfo projectFile, out string relativePath)
    {
        relativePath = string.Empty;
        if (projectFile.DirectoryName == null)
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var projectDirectoryPath = Path.GetFullPath(projectFile.DirectoryName);
        var relative = Path.GetRelativePath(projectDirectoryPath, fullPath);
        if (Path.IsPathRooted(relative) || relative.StartsWith(".."))
        {
            return false;
        }

        relativePath = NormalizePortablePath(relative);
        return true;
    }

    private static bool TrySliceAfterSubpath(string path, string subpath, out string suffix)
    {
        suffix = string.Empty;
        var normalizedPath = NormalizePortablePath(path).TrimEnd('/');
        var normalizedSubpath = NormalizePortablePath(subpath).Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedSubpath))
        {
            return false;
        }

        var exactSuffix = $"/{normalizedSubpath}";
        if (normalizedPath.EndsWith(exactSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var marker = $"/{normalizedSubpath}/";
        var index = normalizedPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        suffix = normalizedPath[(index + marker.Length)..];
        return true;
    }

    private static string ResolveLegacyStorageProjectSubfolder(GeneralPropertiesV0 config, FileInfo projectFile)
    {
        return config.AnnotationBackendMode == AnnotationBackendMode.Cvat && config.ProjectId > 0
            ? $"storage/{ResolveLegacyStorageLeaf(config)}"
            : SanitizePathSegment(Path.GetFileNameWithoutExtension(projectFile.Name));
    }

    private static string ResolveLegacyStorageLeaf(GeneralPropertiesV0 config)
    {
        var host = "project";
        if (!string.IsNullOrWhiteSpace(config.ServerUrl) &&
            Uri.TryCreate(config.ServerUrl, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            host = uri.Host;
        }

        return $"{SanitizePathSegment(host)}_project_{config.ProjectId}";
    }

    private static string SanitizePathSegment(string? value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safe = new string((value ?? "project")
            .Select(x => invalidChars.Contains(x) ? '_' : x)
            .ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(safe) ? "project" : safe;
    }

    private static string NormalizePortablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return string.Join("/", path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ToNativePath(string path)
    {
        return string.Join(Path.DirectorySeparatorChar, path.Split(PortableSeparators, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsRelativePathLike(string path)
    {
        return path.Contains('/') || path.Contains('\\') || path.StartsWith(".", StringComparison.Ordinal);
    }

    private static string GetLastPathSegment(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : NormalizePortablePath(path).Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
    }
}
