using JetBrains.Annotations;

namespace YoloEase.UI.Prism;

[UsedImplicitly]
public sealed class GeneralPropertiesConverter : ConfigMetadataConverter<GeneralPropertiesV2, GeneralProperties>
{
    public override GeneralProperties Convert(GeneralPropertiesV2 value)
    {
        return PrepareClone(value);
    }
}
