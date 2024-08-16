using System.Globalization;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PoeShared.Blazor.Prism;
using PoeShared.Blazor.Wpf.Prism;
using PoeShared.Native;
using PoeShared.Services;
using PoeShared.Squirrel.Prism;
using PoeShared.UI;
using PoeShared.Wpf.Scaffolding;
using Unity.Lifetime;
using YoloEase.UI.Prism;

namespace YoloEase.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : ApplicationBase
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var cultureInfo = new CultureInfo("en");
            CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
            CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

            Container.AddNewExtensionIfNotExists<UpdaterRegistrations>();
            Container.AddNewExtensionIfNotExists<BlazorWebRegistrations>();
            Container.AddNewExtensionIfNotExists<BlazorWpfRegistrations>();
            Container.AddNewExtensionIfNotExists<YoloEaseRegistrations>();
            
            Container
                .RegisterSingleton<IConfigProvider, ConfigProviderFromFile>();
            
            var serviceCollection = Container.Resolve<IServiceCollection>();
            serviceCollection.AddAntDesign();
            serviceCollection.AddBlazorBootstrap();

            var window = Container.Resolve<MainWindow>();
            Container.RegisterOverlayController();
            
            var viewController = new WindowViewController(window);
            Container.RegisterInstance<IWindowViewController>(WellKnownWindows.MainWindow, viewController, new ContainerControlledLifetimeManager());
            window.DataContext = Container.Resolve<MainWindowViewModel>();
            window.Show();
        }
        
    }
}