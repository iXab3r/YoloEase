using System.Linq;
using YoloEase.UI.Scaffolding;

namespace YoloEase.UI.Core;

public class LocalStorageAssetsAccessor : RefreshableReactiveObject, IFileAssetsAccessor
{
    private readonly SourceCacheEx<FileInfo, string> localFileSource = new(x => x.FullName);
    private readonly SourceCacheEx<DirectoryInfo, string> inputDirectoriesSource = new(x => x.FullName);

    private readonly IFileAssetsAccessor assetsAccessor;
    public static readonly string[] FilesFilter = new[] {"*.png", "*.jpg", "*.bmp"};

    public LocalStorageAssetsAccessor(IFileAssetsAccessor assetsAccessor)
    {
        this.assetsAccessor = assetsAccessor;
    }

    public DirectoryInfo StorageDirectory { get; set; }
    
    public IObservableCacheEx<FileInfo, string> Files => localFileSource;

    public ISourceCacheEx<DirectoryInfo, string> InputDirectories => inputDirectoriesSource;
    
    public async Task Refresh()
    {
        if (isBusyLatch.IsBusy)
        {
            throw new InvalidOperationException("Another refresh is already in progress");
        }
        using var isBusy = isBusyLatch.Rent();

        await Task.Run(SynchronizeDirectories);
        await Task.Run(RefreshLocalFiles);
    }

    private async Task SynchronizeDirectories()
    {
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
        }
        
        var storageFilesByStorageName = FilesFilter.Select(x => trainingStorage.GetFiles(x, SearchOption.AllDirectories))
            .SelectMany(x => x)
            .ToDictionary(x => x.Name)
            .ToArray();
        var filesToRemove = storageFilesByStorageName
            .Where(x => !sourceFilesByStorageName.ContainsKey(x.Key))
            .ToArray();

        foreach (var storageFileInfo in filesToRemove)
        {
            Log.Debug($"Removing obsolete storage file {storageFileInfo}");
            try
            {
                File.Delete(storageFileInfo.Value.FullName);
            }
            catch (Exception e)
            {
                Log.Warn($"Failed to remove obsolete storage file {storageFileInfo}", e);
            }
            Log.Debug($"Removed obsolete storage file {storageFileInfo}");
        }

        foreach (var sourceFileInfo in sourceFilesByStorageName.Values)
        {
            Log.Debug($"Processing {sourceFileInfo}");
            try
            {
                var imageSize = ImageUtils.GetImageSize(sourceFileInfo.OriginalFile);
                Log.Debug($"Image metadata for {sourceFileInfo.OriginalFile}: {new { imageSize }}");
                
                if (!sourceFileInfo.StorageFile.Exists)
                {
                    File.Copy(sourceFileInfo.OriginalFile.FullName, sourceFileInfo.StorageFile.FullName);
                }
            }
            catch (Exception e)
            {
                Log.Warn($"Failed to process {sourceFileInfo}", e);
            }
        }
        
        linkedDirectories.Add(trainingStorage);
        inputDirectoriesSource.EditDiff(linkedDirectories);
    }
    
    private async Task RefreshLocalFiles()
    {
        var files = new System.Collections.Generic.HashSet<FileInfo>();

        foreach (var directory in inputDirectoriesSource.Items)
        {
            FilesFilter.Select(x => directory.GetFiles(x)).SelectMany(x => x).ForEach(x => files.Add(x));
        }

        localFileSource.EditDiff(files);
    }
}