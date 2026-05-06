using System.Linq;
using System.Threading;
using PoeShared.Logging;
using YoloDotNet;
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Models;
using YoloEngine = YoloDotNet.Yolo;

namespace YoloEase.UI.TaskAnnotation;

public sealed class YoloEngineRepository : DisposableReactiveObjectWithLogger, IYoloEngineRepository
{
    private static readonly IFluentLog StaticLog = typeof(YoloEngineRepository).PrepareLogger();
    private readonly SourceCache<YoloEngineHandle, string> engines = new(x => x.Key);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ErrorsProvider<YoloEngineRepository> errorsProvider;

    public YoloEngineRepository()
    {
        errorsProvider = new ErrorsProvider<YoloEngineRepository>(this, capacity: 20).AddTo(Anchors);
        Engines = engines.Connect().AsObservableCache().AddTo(Anchors);

        Anchors.Add(() =>
        {
            foreach (var engine in engines.Items.ToArray())
            {
                DisposeEngine(engine);
            }

            engines.Dispose();
        });
    }

    public IObservableCache<YoloEngineHandle, string> Engines { get; }

    public ICanSetErrors ErrorProvider => errorsProvider;

    public async Task<YoloEngineHandle> GetOrLoad(
        AutoAnnotationModelResolution resolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        string key;
        try
        {
            key = BuildModelKey(resolution);
            var cached = engines.Lookup(key);
            if (cached.HasValue)
            {
                Log.Debug($"Using cached YOLO engine for {resolution.ModelFile.FullName} ({resolution.Sha256})");
                return cached.Value;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to inspect YOLO model file {resolution.ModelFile.FullName}", e);
            errorsProvider.Report(e);
            throw;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var cached = engines.Lookup(key);
            if (cached.HasValue)
            {
                Log.Debug($"Using cached YOLO engine for {resolution.ModelFile.FullName} ({resolution.Sha256}) after lock wait");
                return cached.Value;
            }

            Log.Info($"Loading YOLO engine from {resolution.ModelFile.FullName}, hash {resolution.Sha256}");
            var engine = await LoadEngine(resolution, key, cancellationToken);
            engines.AddOrUpdate(engine);
            errorsProvider.ReportSuccess();
            Log.Info($"Loaded YOLO engine {resolution.ModelFile.Name}: {engine.ModelType}, labels: {engine.Labels.Count}");
            return engine;
        }
        catch (OperationCanceledException)
        {
            Log.Info($"Loading YOLO engine was canceled for {resolution.ModelFile.FullName}");
            throw;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load YOLO engine from {resolution.ModelFile.FullName}", e);
            errorsProvider.Report(e);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        try
        {
            var existing = engines.Lookup(key);
            if (!existing.HasValue)
            {
                return;
            }

            engines.RemoveKey(key);
            DisposeEngine(existing.Value);
            Log.Info($"Removed cached YOLO engine {key}");
        }
        catch (Exception e)
        {
            Log.Warn($"Failed to remove cached YOLO engine {key}", e);
            errorsProvider.Report(e);
        }
    }

    private static string BuildModelKey(AutoAnnotationModelResolution resolution)
    {
        try
        {
            resolution.ModelFile.Refresh();
            return $"{resolution.ModelFile.FullName}|{resolution.ModelFile.Length}|{resolution.ModelFile.LastWriteTimeUtc.Ticks}|{resolution.Sha256}";
        }
        catch (Exception e)
        {
            throw new IOException($"Failed to build YOLO model cache key for {resolution.ModelFile.FullName}", e);
        }
    }

    private static async Task<YoloEngineHandle> LoadEngine(
        AutoAnnotationModelResolution resolution,
        string key,
        CancellationToken cancellationToken)
    {
        await EnsureModelFileCanBeOpened(resolution.ModelFile, cancellationToken);

        try
        {
            var yolo = new YoloEngine(new YoloOptions
            {
                ExecutionProvider = new CpuExecutionProvider(resolution.ModelFile.FullName),
            });
            return new YoloEngineHandle(key, resolution, yolo);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"Failed to initialize YOLO CPU engine for {resolution.ModelFile.FullName}.",
                e);
        }
    }

    private static async Task EnsureModelFileCanBeOpened(FileInfo modelFile, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                modelFile.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                useAsync: true);
            var buffer = new byte[1];
            _ = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new IOException($"Failed to open ONNX model {modelFile.FullName}", e);
        }
    }

    private static void DisposeEngine(YoloEngineHandle engine)
    {
        try
        {
            engine.Dispose();
        }
        catch (Exception e)
        {
            StaticLog.Warn($"Failed to dispose YOLO engine {engine.Key}", e);
        }
    }
}
