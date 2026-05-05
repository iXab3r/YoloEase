namespace YoloEase.UI.Services;

/// <summary>
/// Caches loaded YOLO models so repeated predictions can reuse ONNX inference sessions.
/// </summary>
public interface IYoloModelCachingService
{
    DirectoryInfo StorageDirectory { get; set; }
    
    DirectoryInfo CacheDirectory { get; }

    Task<FileInfo> ResolveModelByName(string modelName);
}
