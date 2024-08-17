using System.Xml.Serialization;

namespace YoloEase.Cvat.Shared;

public record CvatBox
{
    [XmlAttribute("label")] public string Label { get; set; }

    [XmlAttribute("source")] public string Source { get; set; }

    [XmlAttribute("occluded")] public int Occluded { get; set; }

    [XmlAttribute("xtl")] public double Xtl { get; set; }

    [XmlAttribute("ytl")] public double Ytl { get; set; }

    [XmlAttribute("xbr")] public double Xbr { get; set; }

    [XmlAttribute("ybr")] public double Ybr { get; set; }

    [XmlAttribute("z_order")] public int ZOrder { get; set; }

    public double Width() => Xbr - Xtl;
    public double Height() => Ybr - Ytl;
	
    public double Area(){
        var width = Xbr - Xtl;
        var height = Ybr - Ytl;
        return width * height;
    }
}