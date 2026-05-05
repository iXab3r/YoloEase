using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using JetBrains.Annotations;
using PoeShared.Dialogs.Services;
using PoeShared.Logging;
using SkiaSharp;
using YoloDotNet.Enums;
using YoloDotNet.Models;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TaskAnnotation;

public sealed class AutoAnnotationAccessor : DisposableReactiveObjectWithLogger, IHasErrorProvider
{
    public const string AutomaticSourcePrefix = "automatic:";

    private readonly SourceListEx<AutoAnnotationModelConfig> modelsSource = new();
    private readonly ErrorsProvider<AutoAnnotationAccessor> errorsProvider;
    private readonly IYoloEngineRepository engineRepository;
    private readonly IOpenFileDialog openFileDialog;
    private readonly IScheduler uiScheduler;
    private readonly SemaphoreSlim runLock = new(1, 1);

    public AutoAnnotationAccessor(
        IYoloEngineRepository engineRepository,
        IOpenFileDialog openFileDialog,
        [Dependency(WellKnownSchedulers.UI)] IScheduler uiScheduler)
    {
        this.engineRepository = engineRepository;
        this.openFileDialog = openFileDialog;
        this.uiScheduler = uiScheduler;

        errorsProvider = new ErrorsProvider<AutoAnnotationAccessor>(this, capacity: 20).AddTo(Anchors);
        errorsProvider.Report(engineRepository.ErrorProvider).AddTo(Anchors);
        Anchors.Add(runLock.Dispose);
    }

    public DirectoryInfo? StorageDirectory { get; [UsedImplicitly] set; }

    public ISourceList<AutoAnnotationModelConfig> Models => modelsSource;

    public ICanSetErrors ErrorProvider => errorsProvider;

    public void LoadModels(IEnumerable<AutoAnnotationModelProperties> models)
    {
        var modelProperties = models.EmptyIfNull().ToArray();
        Log.Info($"Loading {modelProperties.Length} auto-annotation model entries");
        modelsSource.Clear();
        modelsSource.AddRange(modelProperties.Select(AutoAnnotationModelConfig.FromProperties));
        NormalizeOrder();
    }

    public IReadOnlyList<AutoAnnotationModelProperties> SaveModels()
    {
        return modelsSource.Items
            .OrderBy(x => x.Order)
            .Select(x => x.ToProperties())
            .ToArray();
    }

    public AutoAnnotationModelConfig AddLatestModel(string? displayName = null)
    {
        var model = AutoAnnotationModelConfig.CreateLatest(displayName);
        model.Order = modelsSource.Items.Count();
        modelsSource.Add(model);
        Log.Info($"Added latest auto-annotation model entry {model.Id}");
        return model;
    }

    public AutoAnnotationModelConfig DuplicateModel(AutoAnnotationModelConfig source)
    {
        var duplicate = source.CloneAsNewEntry();
        duplicate.Order = modelsSource.Items.Count();
        modelsSource.Add(duplicate);
        NormalizeOrder();
        Log.Info($"Duplicated auto-annotation model entry {source.Id} -> {duplicate.Id}");
        return duplicate;
    }

    public void RemoveModel(AutoAnnotationModelConfig model)
    {
        modelsSource.Remove(model);
        NormalizeOrder();
        Log.Info($"Removed auto-annotation model entry {model.Id}");
    }

    public void MoveModel(AutoAnnotationModelConfig model, int delta)
    {
        var items = modelsSource.Items.OrderBy(x => x.Order).ToList();
        var oldIndex = items.IndexOf(model);
        if (oldIndex < 0)
        {
            return;
        }

        var newIndex = Math.Clamp(oldIndex + delta, 0, items.Count - 1);
        if (newIndex == oldIndex)
        {
            return;
        }

        items.RemoveAt(oldIndex);
        items.Insert(newIndex, model);
        modelsSource.Clear();
        modelsSource.AddRange(items);
        NormalizeOrder();
        Log.Info($"Moved auto-annotation model entry {model.Id} from {oldIndex} to {newIndex}");
    }

    public async Task<AutoAnnotationModelConfig?> ImportCustomModel()
    {
        var selectedFile = await SelectModelFile();
        return selectedFile == null ? null : await ImportCustomModel(selectedFile);
    }

    public async Task<AutoAnnotationModelConfig> ImportCustomModel(FileInfo modelFile)
    {
        try
        {
            if (StorageDirectory == null)
            {
                throw new InvalidOperationException("Save the project before importing custom auto-annotation models.");
            }

            if (!modelFile.Exists)
            {
                throw new FileNotFoundException($"Model file not found @ {modelFile.FullName}");
            }

            Log.Info($"Importing custom auto-annotation model {modelFile.FullName}");
            var storedModel = await CopyModelToStorage(modelFile);
            var model = AutoAnnotationModelConfig.CreateCustom(storedModel.RelativePath, modelFile);
            model.ContentSha256 = storedModel.Sha256;
            RememberResolvedModel(model, new FileInfo(Path.Combine(StorageDirectory.FullName, storedModel.RelativePath)), storedModel.Sha256);
            model.Order = modelsSource.Items.Count();
            modelsSource.Add(model);
            Log.Info($"Imported custom auto-annotation model {modelFile.FullName} -> {storedModel.RelativePath}, hash {storedModel.Sha256}");
            return model;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Error($"Failed to import custom auto-annotation model {modelFile.FullName}", e);
            errorsProvider.Report(e);
            throw;
        }
    }

    public async Task<AutoAnnotationModelResolution> ResolveModel(
        AutoAnnotationModelConfig model,
        IReadOnlyList<TrainedModelFileInfo> trainedModels,
        CancellationToken cancellationToken = default)
    {
        try
        {
            FileInfo modelFile;
            switch (model.SourceKind)
            {
                case AutoAnnotationModelSourceKind.Latest:
                {
                    modelFile = trainedModels
                        .Where(x => x.ModelFile?.Exists == true)
                        .OrderByDescending(x => x.ModelFile.LastWriteTimeUtc)
                        .Select(x => x.ModelFile)
                        .FirstOrDefault();
                    if (modelFile == null)
                    {
                        model.LastStatus = AutoAnnotationModelStatus.MissingFile;
                        model.LastError = "No trained ONNX models were found.";
                        throw new FileNotFoundException(model.LastError);
                    }

                    break;
                }
                case AutoAnnotationModelSourceKind.CustomOnnx:
                {
                    modelFile = await ResolveCustomModelFile(model, cancellationToken);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(model.SourceKind), model.SourceKind, "Unknown auto-annotation model source.");
            }

            modelFile.Refresh();
            if (!modelFile.Exists)
            {
                model.LastStatus = AutoAnnotationModelStatus.MissingFile;
                model.LastError = $"Model file not found @ {modelFile.FullName}";
                throw new FileNotFoundException(model.LastError);
            }

            var sha = TryGetCachedResolvedHash(model, modelFile) ?? TryGetPersistedContentHash(model, modelFile);
            if (sha == null)
            {
                Log.Info($"Computing auto-annotation model hash for {modelFile.FullName}");
                sha = await ComputeSha256(modelFile, cancellationToken);
            }
            else
            {
                Log.Debug($"Using cached auto-annotation model hash for {modelFile.FullName}: {sha}");
            }

            model.LastResolvedModelPath = modelFile.FullName;
            model.LastResolvedModelHash = sha;
            model.LastResolvedModelLength = modelFile.Length;
            model.LastResolvedModelLastWriteTimeUtc = modelFile.LastWriteTimeUtc;
            Log.Info($"Resolved auto-annotation model {model.Id} to {modelFile.FullName}, hash {sha}");
            return new AutoAnnotationModelResolution(modelFile, sha);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            model.LastStatus = model.LastStatus == AutoAnnotationModelStatus.MissingFile
                ? AutoAnnotationModelStatus.MissingFile
                : AutoAnnotationModelStatus.LoadFailed;
            model.LastError = e.Message;
            Log.Warn($"Failed to resolve auto-annotation model {model.DisplayName} ({model.Id})", e);
            errorsProvider.Report(e);
            throw;
        }
    }

    private static string? TryGetCachedResolvedHash(AutoAnnotationModelConfig model, FileInfo modelFile)
    {
        return string.Equals(model.LastResolvedModelPath, modelFile.FullName, StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(model.LastResolvedModelHash) &&
               model.LastResolvedModelLength == modelFile.Length &&
               model.LastResolvedModelLastWriteTimeUtc == modelFile.LastWriteTimeUtc
            ? model.LastResolvedModelHash
            : null;
    }

    private static string? TryGetPersistedContentHash(AutoAnnotationModelConfig model, FileInfo modelFile)
    {
        if (model.SourceKind != AutoAnnotationModelSourceKind.CustomOnnx ||
            string.IsNullOrWhiteSpace(model.ContentSha256) ||
            string.IsNullOrWhiteSpace(model.StorageRelativePath))
        {
            return null;
        }

        var fullPath = NormalizePath(modelFile.FullName);
        var relativePath = NormalizePath(model.StorageRelativePath);
        return fullPath.EndsWith(relativePath, StringComparison.OrdinalIgnoreCase)
            ? model.ContentSha256
            : null;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static void RememberResolvedModel(AutoAnnotationModelConfig model, FileInfo modelFile, string sha)
    {
        modelFile.Refresh();
        model.LastResolvedModelPath = modelFile.FullName;
        model.LastResolvedModelHash = sha;
        model.LastResolvedModelLength = modelFile.Exists ? modelFile.Length : null;
        model.LastResolvedModelLastWriteTimeUtc = modelFile.Exists ? modelFile.LastWriteTimeUtc : null;
    }

    public async Task<AutoAnnotationModelStatus> ValidateModel(
        AutoAnnotationModelConfig model,
        IReadOnlyList<TrainedModelFileInfo> trainedModels,
        IReadOnlyList<AnnotationLabelInfo> projectLabels,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Info($"Validating auto-annotation model {model.DisplayName} ({model.Id})");
            var resolution = await ResolveModel(model, trainedModels, cancellationToken);
            var engine = await engineRepository.GetOrLoad(resolution, cancellationToken);
            if (engine.ModelType != ModelType.ObjectDetection)
            {
                model.LastStatus = AutoAnnotationModelStatus.UnsupportedModel;
                model.LastError = $"Only object-detection models are supported in v1. Detected: {engine.ModelType}.";
                Log.Warn(model.LastError);
                return model.LastStatus;
            }

            EnsureModelLabelMappings(model, engine.Labels, projectLabels);
            model.LastStatus = HasEnabledMappingErrors(model, projectLabels)
                ? AutoAnnotationModelStatus.NeedsMapping
                : AutoAnnotationModelStatus.Ready;
            model.LastError = model.LastStatus == AutoAnnotationModelStatus.Ready
                ? null
                : "One or more enabled model labels are not mapped to a project label.";
            errorsProvider.ReportSuccess();
            Log.Info($"Validated auto-annotation model {model.DisplayName} ({model.Id}): {model.LastStatus}, labels: {engine.Labels.Count}");
            return model.LastStatus;
        }
        catch (OperationCanceledException)
        {
            Log.Info($"Validation canceled for auto-annotation model {model.DisplayName} ({model.Id})");
            throw;
        }
        catch (Exception e)
        {
            model.LastStatus = model.LastStatus == AutoAnnotationModelStatus.MissingFile
                ? AutoAnnotationModelStatus.MissingFile
                : AutoAnnotationModelStatus.LoadFailed;
            model.LastError = e.Message;
            Log.Warn($"Failed to validate auto-annotation model {model.DisplayName} ({model.Id})", e);
            errorsProvider.Report(e);
            return model.LastStatus;
        }
    }

    public async Task<AutoAnnotationRunResult> Run(
        AutoAnnotationRunRequest request,
        IProgress<AutoAnnotationRunProgress>? progress = null,
        Func<AutoAnnotationFrameResult, Task>? frameCompleted = null,
        CancellationToken cancellationToken = default)
    {
        if (!await runLock.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Another auto-annotation run is already in progress.");
        }

        try
        {
            return await Task.Run(async () =>
            {
                try
                {
                    return await RunInternal(request, progress, frameCompleted, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Log.Info($"Auto-annotation run canceled for model {request.Model.DisplayName} ({request.Model.Id})");
                    throw;
                }
                catch (Exception e)
                {
                    request.Model.LastStatus = AutoAnnotationModelStatus.LastRunFailed;
                    request.Model.LastError = e.Message;
                    Log.Error($"Auto-annotation run failed for model {request.Model.DisplayName} ({request.Model.Id})", e);
                    errorsProvider.Report(e);
                    throw;
                }
            }, cancellationToken);
        }
        finally
        {
            runLock.Release();
        }
    }

    public static string CreateShapeSource(string modelEntryId)
    {
        return $"{AutomaticSourcePrefix}{modelEntryId}";
    }

    public static string? GetModelEntryIdFromSource(string? source)
    {
        return !string.IsNullOrWhiteSpace(source) && source.StartsWith(AutomaticSourcePrefix, StringComparison.OrdinalIgnoreCase)
            ? source[AutomaticSourcePrefix.Length..]
            : null;
    }

    public static bool IsAutomaticSource(string? source)
    {
        return GetModelEntryIdFromSource(source) != null;
    }

    private async Task<AutoAnnotationRunResult> RunInternal(
        AutoAnnotationRunRequest request,
        IProgress<AutoAnnotationRunProgress>? progress,
        Func<AutoAnnotationFrameResult, Task>? frameCompleted,
        CancellationToken cancellationToken)
    {
        var model = request.Model;
        model.LastStatus = AutoAnnotationModelStatus.Running;
        model.LastError = null;
        Log.Info($"Starting auto-annotation run for {model.DisplayName} ({model.Id}), frames: {request.Frames.Count}");

        progress?.Report(new AutoAnnotationRunProgress(model, 0, request.Frames.Count, 0, "Resolving model"));
        var resolution = await ResolveModel(model, request.TrainedModels, cancellationToken);
        progress?.Report(new AutoAnnotationRunProgress(model, 0, request.Frames.Count, 0, "Loading model"));
        var engine = await engineRepository.GetOrLoad(resolution, cancellationToken);
        if (engine.ModelType != ModelType.ObjectDetection)
        {
            model.LastStatus = AutoAnnotationModelStatus.UnsupportedModel;
            model.LastError = $"Only object-detection models are supported in v1. Detected: {engine.ModelType}.";
            throw new NotSupportedException(model.LastError);
        }

        progress?.Report(new AutoAnnotationRunProgress(model, 0, request.Frames.Count, 0, "Preparing mappings"));
        EnsureModelLabelMappings(model, engine.Labels, request.ProjectLabels);
        var enabledMappings = BuildEnabledMappings(model, request.ProjectLabels);
        var missingMappings = model.LabelMappings.Items
            .Where(x => x.IsEnabled && !enabledMappings.ContainsKey(x.ModelLabelIndex))
            .ToArray();
        if (missingMappings.Length > 0)
        {
            model.LastStatus = AutoAnnotationModelStatus.NeedsMapping;
            model.LastError = $"Enabled model labels need project mappings: {string.Join(", ", missingMappings.Select(x => x.ModelLabelName))}";
            throw new InvalidOperationException(model.LastError);
        }

        var frames = request.Frames.ToArray();
        var result = new AutoAnnotationRunResult(model.Id, resolution.ModelFile, resolution.Sha256);
        for (var index = 0; index < frames.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = frames[index];
            progress?.Report(new AutoAnnotationRunProgress(model, index, frames.Length, frame.FrameIndex, "Running inference"));

            var frameResult = RunFrame(engine, model, frame, enabledMappings);
            result.FrameResults.Add(frameResult);
            result.AddedCount += frameResult.Predictions.Count;
            result.SkippedCount += frameResult.SkippedCount;

            if (frameCompleted != null)
            {
                try
                {
                    await frameCompleted(frameResult);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    throw new InvalidOperationException($"Failed to merge auto-annotation results for frame {frame.FrameIndex}", e);
                }
            }

            progress?.Report(new AutoAnnotationRunProgress(model, index + 1, frames.Length, frame.FrameIndex, "Frame completed"));
            Log.Debug($"Auto-annotation frame {frame.FrameIndex} completed: {frameResult.Predictions.Count} detections, {frameResult.SkippedCount} skipped");
        }

        model.LastStatus = AutoAnnotationModelStatus.Ready;
        model.LastError = null;
        model.LastRunAt = DateTimeOffset.Now;
        model.LastRunSummary = $"{result.AddedCount} detections, {result.SkippedCount} skipped";
        errorsProvider.ReportSuccess();
        Log.Info($"Auto-annotation run completed for {model.DisplayName} ({model.Id}): added {result.AddedCount}, skipped {result.SkippedCount}");
        return result;
    }

    private AutoAnnotationFrameResult RunFrame(
        YoloEngineHandle engine,
        AutoAnnotationModelConfig model,
        AutoAnnotationFrameInput frame,
        IReadOnlyDictionary<int, AutoAnnotationResolvedLabelMapping> mappings)
    {
        if (!frame.ImageFile.Exists)
        {
            throw new FileNotFoundException($"Frame image not found @ {frame.ImageFile.FullName}");
        }

        using var image = DecodeFrameImage(frame);
        var detections = RunInference(engine, model, frame, image);

        var predictions = new List<AutoAnnotationPrediction>();
        var skippedCount = 0;
        foreach (var detection in detections)
        {
            if (!mappings.TryGetValue(detection.Label.Index, out var mapping))
            {
                skippedCount++;
                continue;
            }

            var box = detection.BoundingBox;
            var x = Clamp(box.Left, 0, image.Width);
            var y = Clamp(box.Top, 0, image.Height);
            var width = Clamp(box.Width, 0, image.Width - x);
            var height = Clamp(box.Height, 0, image.Height - y);
            if (width <= 0 || height <= 0)
            {
                skippedCount++;
                continue;
            }

            predictions.Add(new AutoAnnotationPrediction
            {
                FrameIndex = frame.FrameIndex,
                ProjectLabelId = mapping.ProjectLabel.Id,
                BoundingBox = new RectangleD(x, y, width, height),
                Source = CreateShapeSource(model.Id),
                Confidence = detection.Confidence,
                ModelLabelIndex = detection.Label.Index,
                ModelLabelName = detection.Label.Name,
            });
        }

        return new AutoAnnotationFrameResult(frame.FrameIndex, predictions, skippedCount);
    }

    private SKBitmap DecodeFrameImage(AutoAnnotationFrameInput frame)
    {
        try
        {
            return SKBitmap.Decode(frame.ImageFile.FullName)
                   ?? throw new InvalidOperationException($"Failed to decode frame image {frame.ImageFile.FullName}");
        }
        catch (Exception e)
        {
            throw new IOException($"Failed to decode frame image {frame.ImageFile.FullName}", e);
        }
    }

    private IReadOnlyList<ObjectDetection> RunInference(
        YoloEngineHandle engine,
        AutoAnnotationModelConfig model,
        AutoAnnotationFrameInput frame,
        SKBitmap image)
    {
        try
        {
            Log.Debug($"Running YOLO inference for frame {frame.FrameIndex} with model {model.DisplayName} ({model.Id})");
            return engine.Yolo.RunObjectDetection(
                image,
                confidence: model.ConfidenceThresholdPercentage / 100.0,
                iou: model.IoUThresholdPercentage / 100.0);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"YOLO inference failed for frame {frame.FrameIndex} using model {model.DisplayName}", e);
        }
    }

    private async Task<FileInfo> ResolveCustomModelFile(AutoAnnotationModelConfig model, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(model.StorageRelativePath) && StorageDirectory != null)
            {
                var storedFile = new FileInfo(Path.Combine(StorageDirectory.FullName, model.StorageRelativePath));
                if (storedFile.Exists)
                {
                    return storedFile;
                }
            }

            if (!string.IsNullOrWhiteSpace(model.OriginalPath))
            {
                var originalFile = new FileInfo(model.OriginalPath);
                if (originalFile.Exists && StorageDirectory != null)
                {
                    var storedModel = await CopyModelToStorage(originalFile, cancellationToken);
                    model.StorageRelativePath = storedModel.RelativePath;
                    model.ContentSha256 = storedModel.Sha256;
                    var storedFile = new FileInfo(Path.Combine(StorageDirectory.FullName, storedModel.RelativePath));
                    RememberResolvedModel(model, storedFile, storedModel.Sha256);
                    return storedFile;
                }

                return originalFile;
            }

            model.LastStatus = AutoAnnotationModelStatus.MissingFile;
            model.LastError = "Custom model path is not configured.";
            throw new FileNotFoundException(model.LastError);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new IOException($"Failed to resolve custom model entry {model.DisplayName} ({model.Id})", e);
        }
    }

    private async Task<StoredModelInfo> CopyModelToStorage(FileInfo modelFile, CancellationToken cancellationToken = default)
    {
        if (StorageDirectory == null)
        {
            throw new InvalidOperationException("Storage directory is not configured.");
        }

        try
        {
            var sha = await ComputeSha256(modelFile, cancellationToken);
            var safeName = SanitizeFileName(modelFile.Name);
            var relativePath = Path.Combine("models", "auto-annotation", sha, safeName);
            var targetFile = new FileInfo(Path.Combine(StorageDirectory.FullName, relativePath));
            if (targetFile.Directory is { Exists: false })
            {
                targetFile.Directory.Create();
            }

            if (!targetFile.Exists || targetFile.Length != modelFile.Length)
            {
                await using var source = modelFile.OpenRead();
                await using var target = targetFile.Open(FileMode.Create, FileAccess.Write, FileShare.Read);
                await source.CopyToAsync(target, cancellationToken);
            }

            return new StoredModelInfo(relativePath, sha);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new IOException($"Failed to copy model {modelFile.FullName} into project storage", e);
        }
    }

    private async Task<FileInfo?> SelectModelFile()
    {
        var completion = new TaskCompletionSource<FileInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        uiScheduler.Schedule(() =>
        {
            try
            {
                openFileDialog.Filter = "ONNX model|*.onnx|All files|*.*";
                completion.SetResult(openFileDialog.ShowDialog());
            }
            catch (Exception e)
            {
                Log.Error("Failed to show auto-annotation model file picker", e);
                errorsProvider.Report(e);
                completion.SetException(e);
            }
        });

        return await completion.Task;
    }

    private static void EnsureModelLabelMappings(
        AutoAnnotationModelConfig model,
        IReadOnlyList<LabelModel> modelLabels,
        IReadOnlyList<AnnotationLabelInfo> projectLabels)
    {
        if (modelLabels == null || modelLabels.Count <= 0)
        {
            if (model.LabelMappings.Items.Count() <= 0)
            {
                throw new InvalidOperationException("Model does not expose labels. Add mappings after exporting a model with label metadata.");
            }

            return;
        }

        foreach (var label in modelLabels.OrderBy(x => x.Index))
        {
            var mapping = model.LabelMappings.Items.FirstOrDefault(x => x.ModelLabelIndex == label.Index);
            if (mapping == null)
            {
                var matchingProjectLabel = projectLabels.FirstOrDefault(x => x.Name.Equals(label.Name, StringComparison.OrdinalIgnoreCase));
                mapping = new AutoAnnotationLabelMapping
                {
                    ModelLabelIndex = label.Index,
                    ModelLabelName = label.Name,
                    IsEnabled = true,
                    ProjectLabelId = matchingProjectLabel?.Id,
                    ProjectLabelName = matchingProjectLabel?.Name,
                };
                model.LabelMappings.Add(mapping);
                continue;
            }

            mapping.ModelLabelName = label.Name;
            if (mapping.ProjectLabelId == null && string.IsNullOrWhiteSpace(mapping.ProjectLabelName))
            {
                var matchingProjectLabel = projectLabels.FirstOrDefault(x => x.Name.Equals(label.Name, StringComparison.OrdinalIgnoreCase));
                mapping.ProjectLabelId = matchingProjectLabel?.Id;
                mapping.ProjectLabelName = matchingProjectLabel?.Name;
            }
        }
    }

    private static IReadOnlyDictionary<int, AutoAnnotationResolvedLabelMapping> BuildEnabledMappings(
        AutoAnnotationModelConfig model,
        IReadOnlyList<AnnotationLabelInfo> projectLabels)
    {
        var result = new Dictionary<int, AutoAnnotationResolvedLabelMapping>();
        foreach (var mapping in model.LabelMappings.Items.Where(x => x.IsEnabled))
        {
            var projectLabel = ResolveProjectLabel(mapping, projectLabels);
            if (projectLabel == null)
            {
                continue;
            }

            result[mapping.ModelLabelIndex] = new AutoAnnotationResolvedLabelMapping(mapping, projectLabel);
        }

        return result;
    }

    private static bool HasEnabledMappingErrors(AutoAnnotationModelConfig model, IReadOnlyList<AnnotationLabelInfo> projectLabels)
    {
        return model.LabelMappings.Items.Any(x => x.IsEnabled && ResolveProjectLabel(x, projectLabels) == null);
    }

    private static AnnotationLabelInfo? ResolveProjectLabel(AutoAnnotationLabelMapping mapping, IReadOnlyList<AnnotationLabelInfo> projectLabels)
    {
        if (mapping.ProjectLabelId is { } labelId &&
            projectLabels.FirstOrDefault(x => x.Id == labelId) is { } byId)
        {
            return byId;
        }

        if (!string.IsNullOrWhiteSpace(mapping.ProjectLabelName) &&
            projectLabels.FirstOrDefault(x => x.Name.Equals(mapping.ProjectLabelName, StringComparison.OrdinalIgnoreCase)) is { } byName)
        {
            return byName;
        }

        return null;
    }

    private void NormalizeOrder()
    {
        var index = 0;
        foreach (var model in modelsSource.Items.OrderBy(x => x.Order))
        {
            model.Order = index++;
        }
    }

    private static async Task<string> ComputeSha256(FileInfo file, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = file.OpenRead();
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new IOException($"Failed to compute SHA-256 for {file.FullName}", e);
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(fileName.Select(x => invalidChars.Contains(x) ? '_' : x).ToArray());
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private sealed record StoredModelInfo(string RelativePath, string Sha256);
}
