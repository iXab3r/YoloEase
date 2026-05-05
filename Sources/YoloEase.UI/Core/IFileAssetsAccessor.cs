namespace YoloEase.UI.Core;

/// <summary>
/// Provides access to project image assets that can be added or queried by file name.
/// </summary>
public interface IFileAssetsAccessor
{
    IObservableCacheEx<FileInfo, string> Files { get; }
    ISourceCacheEx<DirectoryInfo, string> InputDirectories { get; }
}
