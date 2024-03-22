using YoloEase.UI.Core;
using YoloEase.UI.Services;
using UnityContainerExtension = Unity.Extension.UnityContainerExtension;

namespace YoloEase.UI.Prism;

internal sealed class YoloEaseRegistrations : UnityContainerExtension
{
    protected override void Initialize()
    {
        Container
            .RegisterType<ICvatClient, CvatClient>();
        
        Container
            .RegisterSingleton<IYoloModelCachingService, YoloModelCachingService>();
    }
}