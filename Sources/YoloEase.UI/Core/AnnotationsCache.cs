namespace YoloEase.UI.Core;

public sealed class AnnotationsCache : DisposableReactiveObject
{
    public DirectoryInfo StorageDirectory { get; set; } 
    
    public FileInfo Get(int taskId)
    {
        if (StorageDirectory == null)
        {
            throw new InvalidOperationException("Storage directory is not set");
        }

        var filePath = Path.Combine(StorageDirectory.FullName, "cache_annotations", $"task_{taskId}", "annotations.xml");
        return new FileInfo(filePath);
    }
}