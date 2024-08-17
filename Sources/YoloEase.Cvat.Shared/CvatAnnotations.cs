using System.Xml.Serialization;

namespace YoloEase.Cvat.Shared;

[XmlRoot("annotations")]
public record CvatAnnotations
{
    [XmlElement("image")] public List<CvatAnnotationImage> Images { get; set; }
}