using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace YoloEase.UI.Dto;

public sealed record DatasetInfo
{
    public bool IsStorage { get; init; }
    
    public FileInfo IndexFile { get; init; }

    public YoloEaseProjectInfo ProjectInfo { get; init; } = new();
    
    public int ImagesTrainingCount { get; init; }
    
    public int ImagesValidationCount { get; init; }
    
    public int ImagesTestCount { get; init; }
    
    public int ImagesCount => ImagesTestCount + ImagesTrainingCount + ImagesValidationCount;
    
    public YoloIndexFile Index { get; init; }
    
    public static DatasetInfo FromIndexFile(
        FileInfo indexFile, 
        DirectoryInfo storageDirectory,
        IConfigSerializer configSerializer)
    {
        var index = ParseIndexFile(indexFile);
        
        var directory = indexFile.Directory;
        if (directory == null)
        {
            throw new ArgumentException($"Directory of index file is not specified: {indexFile}");
        }
        var imagesTrain = new DirectoryInfo(Path.Combine(directory.FullName, "train", "images"));
        var imagesValid = new DirectoryInfo(Path.Combine(directory.FullName, "valid", "images"));
        var imagesTest = new DirectoryInfo(Path.Combine(directory.FullName, "test", "images"));
        
        var projectInfoPath = new FileInfo(Path.Combine(directory.FullName, "cvataat.json"));
        YoloEaseProjectInfo projectInfo;
        if (projectInfoPath.Exists)
        {
            var projectInfoContent = File.ReadAllText(projectInfoPath.FullName);
            projectInfo = configSerializer.Deserialize<YoloEaseProjectInfo>(projectInfoContent);
        }
        else
        {
            projectInfo = null;
        }
        
        return new DatasetInfo()
        {
            Index = index,
            IndexFile = indexFile,
            ImagesTestCount = imagesTest.Exists ? imagesTest.GetFiles().Length : 0,
            ImagesValidationCount = imagesValid.Exists ? imagesValid.GetFiles().Length : 0,
            ImagesTrainingCount = imagesTrain.Exists ? imagesTrain.GetFiles().Length : 0,
            ProjectInfo = projectInfo,
            IsStorage = storageDirectory.IsDirOrSubDir(directory)
        };
    }
    
     private static readonly IDeserializer YamlParser = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)  // Ignore naming convention
            .IgnoreUnmatchedProperties()  // Ignore if there are extra/missing properties
            .Build();
    
    private static YoloIndexFile ParseIndexFile(FileInfo fileInfo)
    {
        using var reader = new StreamReader(fileInfo.FullName);
        var config = YamlParser.Deserialize<YoloIndexFile>(reader);
        return config;
    }
}