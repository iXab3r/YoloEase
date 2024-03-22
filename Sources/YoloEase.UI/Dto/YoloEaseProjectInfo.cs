namespace YoloEase.UI.Dto;

public sealed record YoloEaseProjectInfo : IPoeEyeConfigVersioned
{
    public int Revision { get; set; }
    
    public int ProjectId { get; set; }
    
    public string ServerUrl { get; set; }
    
    public string ProjectUrl { get; set; }
    
    public string ProjectName { get; set; }
    
    public string OrganizationName { get; set; }
    
    public int? OrganizationId { get; set; }
    
    public ModelTrainingSettings ModelTrainingSettings { get; set; }
    
    public int[] Tasks { get; set; }
    
    public string[] Files { get; set; }

    public int Version { get; set; } = 1;
}