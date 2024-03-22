namespace YoloEase.UI.Dto;

public sealed record YoloIndexFile
{
    public string Train { get; set; }
    public string Val { get; set; }
    public string Test { get; set; }
    public int Nc { get; set; }
    public List<string> Names { get; set; }
}