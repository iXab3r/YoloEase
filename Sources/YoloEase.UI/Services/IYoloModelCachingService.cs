namespace YoloEase.UI.Services;

public interface IYoloModelCachingService
{
    DirectoryInfo StorageDirectory { get; set; }
    
    DirectoryInfo CacheDirectory { get; }

    Task<FileInfo> ResolveModelByName(string modelName);
}