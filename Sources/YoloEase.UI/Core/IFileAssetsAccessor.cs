namespace YoloEase.UI.Core;

public interface IFileAssetsAccessor
{
    IObservableCacheEx<FileInfo, string> Files { get; }
    ISourceCacheEx<DirectoryInfo, string> InputDirectories { get; }
}