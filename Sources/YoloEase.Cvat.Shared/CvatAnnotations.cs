using System.Xml.Serialization;

namespace YoloEase.Cvat.Shared;

/// <summary>
/// Represents the CVAT XML annotations document saved for a task or exported dataset.
/// </summary>
[XmlRoot("annotations")]
public record CvatAnnotations
{
    [XmlElement("image")] public List<CvatAnnotationImage> Images { get; set; }
}
