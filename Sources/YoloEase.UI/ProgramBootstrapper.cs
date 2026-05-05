using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using PoeShared.Blazor;
using PoeShared.Blazor.Controls.Services;
using PoeShared.Blazor.Controls.Prism;
using PoeShared.Blazor.Prism;
using PoeShared.Blazor.Scaffolding;
using PoeShared.Blazor.Wpf;
using PoeShared.Blazor.Wpf.Prism;
using PoeShared.Logging;
using PoeShared.Modularity;
using PoeShared.Native;
using PoeShared.Prism;
using PoeShared.Scaffolding;
using PoeShared.Services;
using PoeShared.UI;
using PoeShared.Scaffolding.WPF;
using PoeShared.Wpf.Scaffolding;
using Unity;
using Unity.Lifetime;
using YoloEase.UI.Prism;
using YoloEase.UI.Scaffolding;

namespace YoloEase.UI;

/// <summary>
/// Configures Unity, Blazor, logging, and the WPF shell for the desktop application.
/// </summary>
public sealed class ProgramBootstrapper : DisposableReactiveObject
{
    private static readonly IFluentLog Log = typeof(ProgramBootstrapper).PrepareLogger();

    private readonly ApplicationCore core;

    public ProgramBootstrapper()
    {
        Container = new UnityContainer();
        Container.AddNewExtensionIfNotExists<Diagnostic>();
        Container.AddNewExtensionIfNotExists<CommonRegistrations>();

        core = Container.Resolve<ApplicationCore>().AddTo(Anchors);
    }

    public IUnityContainer Container { get; }

    public void RunAsApp()
    {
        core.InitializeLoggingFromFile();

        var cultureInfo = new CultureInfo("en");
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

        var application = new App();
        core.BindToApplication(application);

        Container.AddNewExtensionIfNotExists<PoeSharedBlazorRegistrations>();
        Container.AddNewExtensionIfNotExists<BlazorWpfRegistrations>();
        Container.AddNewExtensionIfNotExists<YoloEaseRegistrations>();
        Container.RegisterSingleton<IConfigProvider, ConfigProviderFromFile>();

        var blazorContentRepository = Container.Resolve<IBlazorContentRepository>();
        blazorContentRepository.RegisterForJavaScript(typeof(DynamicComponentContainer), "blazor-dynamic-component");
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/AntDesign/js/ant-design-blazor.js"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/AntDesign/css/ant-design-blazor.css"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Wpf/css/bootstrap.css"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Wpf/css/bootstrap-extra.css"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Wpf/css/app.css"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Wpf/css/blazor-window.css"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Wpf/css/font-awesome6.min.css"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Wpf/css/font-play-regular.css"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Wpf/js/jquery-3.3.1.slim.min.js"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Wpf/js/bootstrap.bundle.min.js"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Wpf/js/blazor.utils.js"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Controls/assets/css/main-colors.css"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Controls/assets/css/main-style.css"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"_content/PoeShared.Blazor.Controls/assets/css/main-ant-blazor.css"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo("YoloEase.styles.css"));
        blazorContentRepository.AdditionalFiles.Add(new RefFileInfo(@"assets/css/yoloease.css"));
        
        var serviceCollection = Container.AsServiceCollection();
        serviceCollection.AddAntDesign();
        serviceCollection.AddBlazorBootstrap();
        serviceCollection.AddPoeSharedBlazorControls();

        var uiDispatcher = Container.Resolve<Dispatcher>(WellKnownDispatchers.UI);
        uiDispatcher.BeginInvoke(DispatcherPriority.Send, CreateMainWindow);

        application.InitializeComponent();
        application.Run();
    }

    private void CreateMainWindow()
    {
        Log.Info("Creating Blazor main window");

        var mainWindowViewModel = Container.Resolve<MainWindowViewModel>().AddTo(Anchors);
        var uiDispatcher = Container.Resolve<Dispatcher>(WellKnownDispatchers.UI);
        var blazorWindow = Container.Resolve<IFactory<IBlazorWindow, Dispatcher>>().Create(uiDispatcher);

        blazorWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        blazorWindow.ShowActivated = true;
        blazorWindow.AllowsTransparency = false;
        blazorWindow.Padding = new Thickness(0);
        blazorWindow.BorderThickness = new Thickness(1);
        blazorWindow.ResizeMode = ResizeMode.CanResize;
        blazorWindow.Width = 1200;
        blazorWindow.Height = 600;
        blazorWindow.TitleBarDisplayMode = TitleBarDisplayMode.System;
        blazorWindow.ViewType = typeof(MainWindowComponent);
        blazorWindow.ViewTypeForTitleBar = typeof(MainWindowTitlebar);
        blazorWindow.Title = mainWindowViewModel.Title;
        blazorWindow.DataContext = mainWindowViewModel;
        blazorWindow.AdditionalFiles = mainWindowViewModel.AdditionalFiles.ToImmutableArray();

        mainWindowViewModel
            .WhenAnyValue(x => x.Title)
            .Subscribe(title => blazorWindow.Title = title)
            .AddTo(Anchors);

        Container.RegisterOverlayController();

        var viewController = new BlazorWindowViewController(blazorWindow);
        Container.RegisterInstance<IWindowViewController>(WellKnownWindows.MainWindow, viewController, new ContainerControlledLifetimeManager());

        if (blazorWindow is IBlazorWindowMetroController blazorWindowMetroController)
        {
            var nativeWindow = blazorWindowMetroController.GetWindow();
            nativeWindow
                .WhenLoaded()
                .Take(1)
                .Subscribe(_ => Application.Current.MainWindow = nativeWindow)
                .AddTo(Anchors);
        }

        blazorWindow
            .WhenLoaded
            .Take(1)
            .Subscribe(_ => blazorWindow.Activate())
            .AddTo(Anchors);

        blazorWindow.Show();
    }
}
