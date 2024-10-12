using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using YoloEase.UI.Scaffolding;

namespace YoloEase.UI.Core;

public class LocalStorageAssetsAccessor : RefreshableReactiveObject, IFileAssetsAccessor
{
    public static readonly string[] FilesFilter = new[] {"*.png", "*.jpg", "*.bmp"};

    private readonly SourceCacheEx<FileInfo, string> localFileSource = new(x => x.FullName);
    private readonly SourceCacheEx<DirectoryInfo, string> inputDirectoriesSource = new(x => x.FullName);

    private readonly DataSourcesProvider assetsAccessor;

    public LocalStorageAssetsAccessor(DataSourcesProvider assetsAccessor)
    {
        this.assetsAccessor = assetsAccessor;
    }

    public DirectoryInfo StorageDirectory { get; set; }
    
    public IObservableCacheEx<FileInfo, string> Files => localFileSource;

    public ISourceCacheEx<DirectoryInfo, string> InputDirectories => inputDirectoriesSource;

    protected override async Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
        using var progressTracker = new ComplexProgressTracker(progressReporter ?? new SimpleProgressReporter());
        
        await Task.Run(() => SynchronizeDirectories(progressTracker.GetOrAdd(nameof(SynchronizeDirectories))));
        await Task.Run(() => RefreshLocalFiles(progressTracker.GetOrAdd(nameof(RefreshLocalFiles))));
    }

    private async Task SynchronizeDirectories(IProgressReporter progressReporter)
    {
        using var progressTracker = new ComplexProgressTracker(progressReporter);
        
        var directoriesScanReporter = progressTracker.GetOrAdd("Directories Scanning");
        var filesCleanupReporter = progressTracker.GetOrAdd("Files cleanup");
        var imageScanReporter = progressTracker.GetOrAdd("Scanning images");
        
        var inputDirectories = assetsAccessor.InputDirectories.Items.ToArray();
        var storage = new DirectoryInfo(Path.Combine(StorageDirectory.FullName, "assets"));
        if (!storage.Exists)
        {
            storage.Create();
        }

        var trainingStorage = new DirectoryInfo(Path.Combine(storage.FullName, "training"));
        if (!trainingStorage.Exists)
        {
            trainingStorage.Create();
        }
        
        var sourceFilesByStorageName = new Dictionary<string, (FileInfo OriginalFile, FileInfo StorageFile)>();
        var linkedDirectories = new List<DirectoryInfo>();
        
        for (var i = 0; i < inputDirectories.Length; i++)
        {
            var directory = inputDirectories[i];
            var directoryId = $"dir{i}";

            var directoryFiles = FilesFilter.Select(x => directory.GetFiles(x, SearchOption.AllDirectories))
                .SelectMany(x => x)
                .ToArray();

            foreach (var fileToLink in directoryFiles)
            {
                /*
                 There is a problem with symlinks - Python resolves them to real file names
                var folderLink = new DirectoryInfo(Path.Combine(subdirectory.FullName, relativeFolder));
                if (!folderLink.Exists)
                {
                    folderLink.Create();
                }

                var fileLink = new FileInfo(Path.Combine(folderLink.FullName, fileToLink.Name));
                if (!fileLink.Exists)
                {
                    File.CreateSymbolicLink(fileLink.FullName, fileToLink.FullName);
                }*/
                
                var relativeFolder = Path.GetRelativePath(directory.FullName, fileToLink.DirectoryName!);
                var folderPrefix = string.IsNullOrEmpty(relativeFolder) || relativeFolder == "." 
                    ? string.Empty
                    : $"{relativeFolder.Replace('/', '_').Replace('\\', '_')}_";

                var filePrefix = $"{directoryId}_{folderPrefix}";

                var storageFileName = fileToLink.Name.StartsWith(filePrefix)
                    ? fileToLink.Name
                    : $"{filePrefix}{fileToLink.Name}";

                var storageFile = new FileInfo(Path.Combine(trainingStorage.FullName, storageFileName));
                sourceFilesByStorageName.Add(storageFile.Name, (OriginalFile: fileToLink, StorageFile: storageFile));
            }
            
            directoriesScanReporter.Update(i+1, inputDirectories.Length);
        }
        
        var storageFilesByStorageName = FilesFilter.Select(x => trainingStorage.GetFiles(x, SearchOption.AllDirectories))
            .SelectMany(x => x)
            .ToDictionary(x => x.Name)
            .ToArray();
        var filesToRemove = storageFilesByStorageName
            .Where(x => !sourceFilesByStorageName.ContainsKey(x.Key))
            .ToArray();

        for (var i = 0; i < filesToRemove.Length; i++)
        {
            var storageFileInfo = filesToRemove[i];
            Log.Debug($"Removing obsolete storage file {storageFileInfo}");
            try
            {
                File.Delete(storageFileInfo.Value.FullName);
            }
            catch (Exception e)
            {
                Log.Warn($"Failed to remove obsolete storage file {storageFileInfo}", e);
            }
            finally
            {
                Log.Debug($"Removed obsolete storage file {storageFileInfo}");
                filesCleanupReporter.Update(i+1, filesToRemove.Length);
            }
        }

        var images = sourceFilesByStorageName.Values.ToArray();
        var processedImages = 0;
        await Parallel.ForAsync(0, images.Length, new ParallelOptions(), async (i, token) =>
        {
            var sourceFileInfo = images[i];
            Log.Debug($"Processing image {sourceFileInfo}");
            
            try
            {
                var imageSize = ImageUtils.GetImageSize(sourceFileInfo.OriginalFile);
                Log.Debug($"Image metadata for {sourceFileInfo.OriginalFile}: {new {imageSize}}");

                if (!sourceFileInfo.StorageFile.Exists)
                {
                    File.Copy(sourceFileInfo.OriginalFile.FullName, sourceFileInfo.StorageFile.FullName);
                }
            }
            catch (Exception e)
            {
                Log.Warn($"Failed to process image {sourceFileInfo}", e);
            }
            finally
            {
                imageScanReporter.Update(Interlocked.Increment(ref processedImages), images.Length);
            }
        });
        
        linkedDirectories.Add(trainingStorage);
        inputDirectoriesSource.EditDiff(linkedDirectories);
    }
    
    private async Task RefreshLocalFiles(IProgressReporter progressReporter)
    {
        var files = new ConcurrentDictionary<string, FileInfo>();
        var filters = new[] {"*.png", "*.jpg", "*.bmp"};
        var directoriesToProcess = inputDirectoriesSource.Items.ToArray();
        var processedDirectories = 0;
        await Parallel.ForEachAsync(directoriesToProcess, new ParallelOptions(), async (directory, token) =>
        {
            filters
                .Select(directory.GetFiles)
                .SelectMany(x => x)
                .ForEach(x => files[x.FullName] = x);
            var count = Interlocked.Increment(ref processedDirectories);
            progressReporter.Update(count, directoriesToProcess.Length);
        });

        localFileSource.EditDiff(files.Values);
    }
}