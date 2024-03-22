using JetBrains.Annotations;
using YoloEase.UI.Dto;

namespace YoloEase.UI.Prism;

[UsedImplicitly]
public sealed class MetadataReplacements : IPoeConfigMetadataReplacementProvider
{
    public IEnumerable<MetadataReplacement> Replacements { get; } = new[]
    {
        MetadataReplacement.ForType<GeneralProperties>("CVATAAT.UI.GeneralProperties") ,
        MetadataReplacement.ForType<YoloEaseProjectInfo>("CVATAAT.UI.Dto.CvataatProjectInfo") ,
    };
}