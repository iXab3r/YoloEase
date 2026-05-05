using JetBrains.Annotations;

namespace YoloEase.UI.Prism;

[UsedImplicitly]
public sealed class GeneralPropertiesConverterV0ToV2 : ConfigMetadataConverter<GeneralPropertiesV0, GeneralPropertiesV2>
{
    public override GeneralPropertiesV2 Convert(GeneralPropertiesV0 value)
    {
        return PrepareClone(value);
    }
}
