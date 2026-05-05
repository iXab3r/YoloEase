using JetBrains.Annotations;
using YoloEase.UI.Dto;

namespace YoloEase.UI.Prism;

/// <summary>
/// Supplies metadata replacement values used by PoeEye configuration serialization.
/// </summary>
[UsedImplicitly]
public sealed class MetadataReplacements : IPoeConfigMetadataReplacementProvider
{
    public IEnumerable<MetadataReplacement> Replacements { get; } = new[]
    {
        MetadataReplacement.ForType<GeneralProperties>("CVATAAT.UI.GeneralProperties") ,
        MetadataReplacement.ForType<YoloEaseProjectInfo>("CVATAAT.UI.Dto.CvataatProjectInfo") ,
    };
}
