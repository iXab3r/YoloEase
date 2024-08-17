using System.Xml.Serialization;

namespace YoloEase.Cvat.Shared;

public record CvatAnnotationImage
{
    [XmlAttribute("id")] public int Id { get; set; }

    [XmlAttribute("name")] public string Name { get; set; }

    [XmlAttribute("subset")] public string Subset { get; set; }

    [XmlAttribute("task_id")] public int TaskId { get; set; }

    [XmlAttribute("width")] public int Width { get; set; }

    [XmlAttribute("height")] public int Height { get; set; }

    [XmlElement("box")] public List<CvatBox> Boxes { get; set; }
}