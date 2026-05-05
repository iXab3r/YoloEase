using YoloEase.UI.Core;
using YoloEase.UI.Prerequisites;
using YoloEase.UI.Services;
using UnityContainerExtension = Unity.Extension.UnityContainerExtension;

namespace YoloEase.UI.Prism;

/// <summary>
/// Registers YoloEase services, view models, and prerequisite helpers with Unity.
/// </summary>
internal sealed class YoloEaseRegistrations : UnityContainerExtension
{
    protected override void Initialize()
    {
        Container
            .RegisterType<ICvatClient, CvatClient>();
        
        Container
            .RegisterSingleton<IYoloModelCachingService, YoloModelCachingService>()
            .RegisterSingleton<IPrerequisitesToolchain, PrerequisitesToolchain>()
            .RegisterSingleton<IPrerequisiteCommandRunner, PrerequisiteCommandRunner>()
            .RegisterSingleton<IGpuRuntimeDetector, GpuRuntimeDetector>();

        PoeShared.Scaffolding.UnityContainerExtensions.RegisterSingleton<PrerequisitesInstaller>(Container);
        PoeShared.Scaffolding.UnityContainerExtensions.RegisterSingleton<PrerequisitesSuiteFactory>(Container);
        Container.RegisterSingleton<PrerequisitesViewModel>(x => new PrerequisitesViewModel(
            x.Resolve<IConfigProvider<YoloEaseApplicationConfig>>(),
            x.Resolve<IPrerequisitesToolchain>(),
            x.Resolve<PrerequisitesSuiteFactory>()));
    }
}
