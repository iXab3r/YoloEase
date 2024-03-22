using System.Linq;

namespace YoloEase.UI.Core;

public class FileSystemAssetsAccessor : RefreshableReactiveObject, IFileAssetsAccessor
{
    private readonly SourceCacheEx<FileInfo, string> localFileSource = new(x => x.FullName);
    private readonly SourceCacheEx<DirectoryInfo, string> inputDirectoriesSource = new(x => x.FullName);

    public FileSystemAssetsAccessor( )
    {
        inputDirectoriesSource
            .Connect()
            .SubscribeAsync(async (_, _) => await RefreshLocalFiles())
            .AddTo(Anchors);
    }

    public IObservableCacheEx<FileInfo, string> Files => localFileSource;

    public ISourceCacheEx<DirectoryInfo, string> InputDirectories => inputDirectoriesSource;

    public async Task Refresh()
    {
        if (isBusyLatch.IsBusy)
        {
            throw new InvalidOperationException("Another refresh is already in progress");
        }
        using var isBusy = isBusyLatch.Rent();

        await Task.Run(RefreshLocalFiles);
    }

    public async Task RefreshLocalFiles()
    {
        var files = new System.Collections.Generic.HashSet<FileInfo>();

        var filters = new[] {"*.png", "*.jpg", "*.bmp"};
        foreach (var directory in inputDirectoriesSource.Items)
        {
            filters.Select(x => directory.GetFiles(x)).SelectMany(x => x).ForEach(x => files.Add(x));
        }

        localFileSource.EditDiff(files);
    }
}