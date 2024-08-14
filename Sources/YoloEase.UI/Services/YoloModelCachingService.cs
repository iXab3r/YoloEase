using System.Threading;
using Humanizer.Bytes;
using JetBrains.Annotations;
using PoeShared.Logging;
using PoeShared.Squirrel.Core;

namespace YoloEase.UI.Services;

public class YoloModelCachingService : DisposableReactiveObjectWithLogger, IYoloModelCachingService
{
    private static readonly Binder<YoloModelCachingService> Binder = new();

    private static readonly Uri BaseDownloadUri = new Uri(@"https://eyeauras.blob.core.windows.net/eyeauras-ml/yolov8", UriKind.Absolute);

    static YoloModelCachingService()
    {
        Binder.Bind(x => x.StorageDirectory == null ? null : new DirectoryInfo(Path.Combine(x.StorageDirectory.FullName, "modelCache"))).To(x => x.CacheDirectory);
    }

    private readonly IFileDownloader fileDownloader;
    private readonly SemaphoreSlim mutex = new(1);

    public YoloModelCachingService(IFileDownloader fileDownloader)
    {
        this.fileDownloader = fileDownloader;

        Binder.Attach(this).AddTo(Anchors);
    }
    
    public DirectoryInfo StorageDirectory { get; set; }
    
    public DirectoryInfo CacheDirectory { get; [UsedImplicitly] private set; }
    
    public async Task<FileInfo> ResolveModelByName(string modelName)
    {
        var log = Log.WithSuffix(modelName);
        var cacheDirectory = CacheDirectory;
        
        log.Debug($"Resolving model {modelName} from cache, directory: {cacheDirectory}");
        await mutex.WaitAsync();

        if (cacheDirectory == null)
        {
            throw new InvalidOperationException("Cache directory is not set");
        }

        try
        {
            var localModelPath = Path.Combine(cacheDirectory.FullName, modelName);
            var localModel = new FileInfo(localModelPath);
            log.Debug($"Cached model path: {localModel.FullName} (exists: {localModel.Exists})");

            if (localModel.Exists)
            {
                return localModel;
            }
            log.Debug($"Model {modelName} does not in the cache yet, downloading it");
            await DownloadModel(log, modelName, localModel);
            log.Debug($"Model '{modelName}' size: {ByteSize.FromBytes(localModel.Length)}");
            return localModel;
        }
        finally
        {
            mutex.Release();
        }
    }

    private async Task DownloadModel(IFluentLog log, string modelName, FileInfo outputFile)
    {
        //FIXME This code is not multi-program safe, it could potentially mess with other copies downloading the same file
        var modelUri = new Uri($"{BaseDownloadUri}/{modelName}");

        var tmpFilePath = Path.ChangeExtension(outputFile.FullName, "tmp");
        
        try
        {
            log.Debug($"Downloading the model {modelName} from {modelUri} to temp file {tmpFilePath}");
            await fileDownloader.DownloadFile(modelUri.ToString(), tmpFilePath, progressPercent => { log.Debug($"Download progress: {progressPercent}%"); });
            var tmpFile = new FileInfo(tmpFilePath);
            log.Debug($"Download completed into {tmpFile.FullName}, exists: {tmpFile.Exists}");
            if (!tmpFile.Exists)
            {
                throw new FileNotFoundException($"Failed to download model '{modelName}' from {modelUri} to {tmpFile.FullName}");
            }

            if (File.Exists(outputFile.FullName))
            {
                log.Debug($"Cleaning up existing model @ {outputFile.FullName}");
                File.Delete(outputFile.FullName);
            }

            log.Debug($"Moving downloaded model into {outputFile.FullName}");
            tmpFile.MoveTo(outputFile.FullName);
        }
        catch (Exception e)
        {
            Log.Error($"Exception occurred while tried to download model from {modelUri} to {tmpFilePath}", e);
            throw;
        }
        finally
        {
            if (File.Exists(tmpFilePath))
            {
                Log.Debug($"Cleaning up temporary file from {tmpFilePath}");
                File.Delete(tmpFilePath);
            }
        }
       
        outputFile.Refresh();
        if (!outputFile.Exists)
        {
            throw new FileNotFoundException($"Failed to download model {modelName} into {outputFile.FullName}");
        }
    }
    
    

    private static DirectoryInfo ParseToDirectoryOrDefault(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return default;
        }

        var directory = new DirectoryInfo(path);
        return directory;
    }

}