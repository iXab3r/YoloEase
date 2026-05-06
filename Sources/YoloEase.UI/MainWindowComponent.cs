using System.Linq;
using System.Reactive.Disposables;
using AntDesign;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using PoeShared.Blazor.Scaffolding;
using PoeShared.Blazor.Services;
using PoeShared.Blazor.Controls.GoldenLayout;
using PoeShared.Blazor.Wpf;
using PoeShared.Common;
using YoloEase.UI.Controls;
using YoloEase.UI.Core;

namespace YoloEase.UI;

/// <summary>
/// Base class for Blazor components backed by refreshable YoloEase view models.
/// </summary>
public abstract class YoloEaseComponent<T> : PoeShared.Blazor.BlazorReactiveComponent<T> where T : RefreshableReactiveObject
{
}

/// <summary>
/// Coordinates the main window Blazor surface with the GoldenLayout-backed tab workspace.
/// </summary>
public partial class MainWindowComponent : YoloEaseComponent<MainWindowViewModel>
{
    private const string MainPanelId = "YoloEaseMainPanel";
    private static readonly TimeSpan LayoutJsTimeout = TimeSpan.FromSeconds(3);

    private static readonly object LayoutMain = new
    {
        root = new
        {
            type = "column",
            content = new object[]
            {
                new
                {
                    type = "stack",
                    isClosable = false,
                    id = MainPanelId,
                    content = Array.Empty<object>(),
                },
            },
        },
    };

    private readonly Dictionary<string, object?> registeredTabContexts = new();
    private readonly Dictionary<string, string?> registeredTabTitles = new();
    private CompositeDisposable? layoutSubscriptions;
    private IGoldenLayoutFacade? goldenLayout;
    private IGoldenLayoutBlazorAdapter? goldenLayoutBlazorAdapter;
    private MainWindowViewModel? currentLayoutContext;
    private ElementReference appContainerElement;
    private bool isInitializingLayout;
    private bool isLayoutLoading;
    private bool isSyncingLayout;
    private bool isPreparingLayoutTabs;
    private bool isProjectDropTargetRegistered;

    public MainWindowComponent()
    {
        ChangeTrackers.Add(this.WhenAnyValue(x => x.DataContext.YoloEaseProject));
        ChangeTrackers.Add(this.WhenAnyValue(x => x.DataContext.IsAdvancedMode));
        ChangeTrackers.Add(this.WhenAnyValue(x => x.DataContext.Tabs).Select(x => x.Connect()).Select(x => x.WhenValueChanged(x => x.IsVisible)));
    }

    [Inject]
    private IGoldenLayoutInterop GoldenLayoutInterop { get; init; } = default!;

    [Inject]
    private IServiceProvider Services { get; init; } = default!;

    [Inject]
    private ICoreWebView2Accessor CoreWebView2Accessor { get; init; } = default!;

    [Inject]
    private IJsPoeBlazorUtils JsPoeBlazorUtils { get; init; } = default!;

    [Inject]
    private INotificationService NotificationService { get; init; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (firstRender)
        {
            await RegisterProjectDropTarget();
        }

        if (DataContext?.YoloEaseProject.IsEmpty != false)
        {
            await DisposeGoldenLayout();
            return;
        }

        if (goldenLayout == null || !ReferenceEquals(currentLayoutContext, DataContext))
        {
            await InitializeGoldenLayout();
            return;
        }

        await SyncGoldenLayoutTabs();
    }

    private async Task RegisterProjectDropTarget()
    {
        if (isProjectDropTargetRegistered)
        {
            return;
        }

        try
        {
            CoreWebView2Accessor.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            await JsPoeBlazorUtils.RegisterFileDropTarget(appContainerElement);
            isProjectDropTargetRegistered = true;
            Log.Info("Registered project file drop target");
        }
        catch (Exception e)
        {
            try
            {
                CoreWebView2Accessor.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            }
            catch (Exception detachException)
            {
                Log.Warn("Failed to detach project file drop handler after registration failure", detachException);
            }

            Log.Warn("Failed to register project file drop target", e);
            ShowDropError("Failed to enable drag-and-drop for project files.");
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (e.AdditionalObjects == null)
            {
                return;
            }

            var projectFile = e.AdditionalObjects
                .OfType<CoreWebView2File>()
                .Select(x => x.Path)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .Select(x => new FileInfo(x))
                .FirstOrDefault(IsProjectFile);
            if (projectFile == null)
            {
                return;
            }

            InvokeAsync(() => OpenDroppedProject(projectFile)).AndForget();
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to read dropped project file", ex);
            ShowDropError($"Failed to read dropped project file: {ex.Message}");
        }
    }

    private async Task OpenDroppedProject(FileInfo projectFile)
    {
        try
        {
            Log.Info($"Opening dropped project file {projectFile.FullName}");
            if (await DataContext.OpenProjectFile(projectFile))
            {
                NotificationService.Open(new NotificationConfig
                {
                    NotificationType = NotificationType.Success,
                    Duration = 4,
                    Message = $"Opened project {projectFile.Name}",
                }).AndForget();
            }
        }
        catch (Exception e)
        {
            Log.Warn($"Failed to open dropped project file {projectFile.FullName}", e);
            ShowDropError($"Failed to open dropped project: {e.Message}");
        }
    }

    private static bool IsProjectFile(FileInfo file)
    {
        return string.Equals(file.Extension, ".yeproj", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowDropError(string message)
    {
        NotificationService.Open(new NotificationConfig
        {
            NotificationType = NotificationType.Error,
            Duration = 8,
            Message = message,
        }).AndForget();
    }

    private async Task InitializeGoldenLayout()
    {
        if (isInitializingLayout)
        {
            return;
        }

        isInitializingLayout = true;
        await SetLayoutLoading(true);
        try
        {
            Log.Debug("Initializing GoldenLayout workspace");
            await DisposeGoldenLayout();

            currentLayoutContext = DataContext;
            layoutSubscriptions = new CompositeDisposable();
            goldenLayout = await WithLayoutTimeout(GoldenLayoutInterop.Create(ElementRef).AsTask(), "create GoldenLayout");
            goldenLayoutBlazorAdapter = Services.GetRequiredService<IGoldenLayoutBlazorAdapter>();
            await WithLayoutTimeout(goldenLayoutBlazorAdapter.Initialize(goldenLayout).AsTask(), "initialize GoldenLayout adapter");
            await WithLayoutTimeout(goldenLayout.LoadLayout(LayoutMain).AsTask(), "load GoldenLayout layout");

            var hook = goldenLayout.AddHook().Publish();
            hook.Connect().AddTo(layoutSubscriptions);
            hook
                .Select(x => x.WhenFocused)
                .Switch()
                .Subscribe(HandleFocusChanged)
                .AddTo(layoutSubscriptions);

            DataContext.Tabs
                .Connect()
                .AutoRefresh(x => x.IsVisible)
                .AutoRefresh(x => x.Title)
                .AutoRefresh(x => x.DataContext)
                .Subscribe(_ => SyncGoldenLayoutTabs().AsTask().AndForget(ignoreExceptions: true))
                .AddTo(layoutSubscriptions);

            DataContext
                .WhenAnyValue(x => x.ActiveTabId)
                .Where(x => !string.IsNullOrEmpty(x))
                .Subscribe(x => FocusGoldenLayoutTab(x).AsTask().AndForget(ignoreExceptions: true))
                .AddTo(layoutSubscriptions);

            await SyncGoldenLayoutTabs();
            Log.Debug("GoldenLayout workspace initialized");
        }
        catch (Exception e)
        {
            Log.Warn("Failed to initialize GoldenLayout workspace", e);
            await DisposeGoldenLayout();
        }
        finally
        {
            isInitializingLayout = false;
            await SetLayoutLoading(false);
        }
    }

    private async ValueTask SyncGoldenLayoutTabs()
    {
        if (goldenLayout == null || goldenLayoutBlazorAdapter == null || isSyncingLayout)
        {
            return;
        }

        var visibleTabs = DataContext.Tabs.Items
            .Where(x => x.IsVisible && x.DataContext != null)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Title)
            .ToArray();
        var visibleTabIds = visibleTabs.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var needsActiveTabSelection = string.IsNullOrEmpty(DataContext.ActiveTabId)
            ? visibleTabs.Length > 0
            : visibleTabs.All(x => x.Id != DataContext.ActiveTabId);
        var needsTabSync =
            needsActiveTabSelection ||
            registeredTabContexts.Keys.Any(x => !visibleTabIds.Contains(x)) ||
            visibleTabs.Any(tab =>
                !registeredTabContexts.TryGetValue(tab.Id, out var registeredContext) ||
                !registeredTabTitles.TryGetValue(tab.Id, out var registeredTitle) ||
                !ReferenceEquals(registeredContext, tab.DataContext) ||
                !string.Equals(registeredTitle, tab.Title, StringComparison.Ordinal));

        if (!needsTabSync)
        {
            return;
        }

        isSyncingLayout = true;
        try
        {
            var adapter = goldenLayoutBlazorAdapter;

            isPreparingLayoutTabs = true;
            try
            {
                foreach (var staleTabId in registeredTabContexts.Keys.Where(x => !visibleTabIds.Contains(x)).ToArray())
                {
                    await RemoveGoldenLayoutTab(staleTabId);
                }

                foreach (var tab in visibleTabs)
                {
                    if (registeredTabContexts.TryGetValue(tab.Id, out var registeredContext) &&
                        registeredTabTitles.TryGetValue(tab.Id, out var registeredTitle) &&
                        ReferenceEquals(registeredContext, tab.DataContext) &&
                        string.Equals(registeredTitle, tab.Title, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (registeredTabContexts.ContainsKey(tab.Id))
                    {
                        await RemoveGoldenLayoutTab(tab.Id);
                    }

                    await WithLayoutTimeout(adapter.AddChild(
                        MainPanelId,
                        new GLBlazorComponentState
                        {
                            Id = tab.Id,
                            Title = tab.Title,
                            Closeable = false,
                            ReorderEnabled = true,
                        },
                        dataContext: tab.DataContext,
                        bodyViewType: tab.ViewType).AsTask(), $"add GoldenLayout tab {tab.Id}");

                    registeredTabContexts[tab.Id] = tab.DataContext;
                    registeredTabTitles[tab.Id] = tab.Title;
                }

                if (string.IsNullOrEmpty(DataContext.ActiveTabId) || visibleTabs.All(x => x.Id != DataContext.ActiveTabId))
                {
                    DataContext.ActiveTabId = visibleTabs.FirstOrDefault()?.Id ?? string.Empty;
                }
            }
            finally
            {
                isPreparingLayoutTabs = false;
            }

            if (!string.IsNullOrEmpty(DataContext.ActiveTabId))
            {
                await FocusGoldenLayoutTab(DataContext.ActiveTabId);
            }
        }
        catch (Exception e)
        {
            Log.Warn("Failed to sync GoldenLayout tabs; resetting workspace layout", e);
            await DisposeGoldenLayout();
        }
        finally
        {
            isSyncingLayout = false;
        }
    }

    private async ValueTask RemoveGoldenLayoutTab(string tabId)
    {
        try
        {
            if (goldenLayoutBlazorAdapter != null)
            {
                await WithLayoutTimeout(goldenLayoutBlazorAdapter.Remove(tabId).AsTask(), $"remove GoldenLayout tab {tabId}");
            }
        }
        finally
        {
            registeredTabContexts.Remove(tabId);
            registeredTabTitles.Remove(tabId);
        }
    }

    private async ValueTask FocusGoldenLayoutTab(string? tabId)
    {
        if (goldenLayout == null || isPreparingLayoutTabs || string.IsNullOrEmpty(tabId) || !registeredTabContexts.ContainsKey(tabId))
        {
            return;
        }

        await WithLayoutTimeout(goldenLayout.FocusById(tabId).AsTask(), $"focus GoldenLayout tab {tabId}");
    }

    private void HandleFocusChanged(string componentId)
    {
        if (isPreparingLayoutTabs ||
            (isSyncingLayout && !string.Equals(componentId, DataContext.ActiveTabId, StringComparison.Ordinal)))
        {
            return;
        }

        var tab = DataContext.Tabs.Items.FirstOrDefault(x => x.Id == componentId);
        if (tab == null)
        {
            return;
        }

        DataContext.ActiveTabId = tab.Id;

        foreach (var otherTab in DataContext.Tabs.Items)
        {
            otherTab.IsSelected = false;
            if (otherTab.DataContext is ICanBeSelected selectable)
            {
                selectable.IsSelected = false;
            }
        }

        tab.IsSelected = true;
        if (tab.DataContext is ICanBeSelected selectedContent)
        {
            selectedContent.IsSelected = true;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            CoreWebView2Accessor.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
        catch (Exception e)
        {
            Log.Warn("Failed to detach project file drop handler", e);
        }

        await DisposeGoldenLayout();
        await base.DisposeAsync();
    }

    private async ValueTask DisposeGoldenLayout()
    {
        layoutSubscriptions?.DisposeJsSafe();
        layoutSubscriptions = null;

        registeredTabContexts.Clear();
        registeredTabTitles.Clear();
        goldenLayoutBlazorAdapter?.DisposeJsSafe();
        goldenLayoutBlazorAdapter = null;

        if (goldenLayout == null)
        {
            currentLayoutContext = null;
            return;
        }

        var layoutToDispose = goldenLayout;
        goldenLayout = null;
        currentLayoutContext = null;
        try
        {
            Log.Debug("Disposing GoldenLayout workspace");
            await WithLayoutTimeout(layoutToDispose.DisposeJsSafeAsync().AsTask(), "dispose GoldenLayout");
        }
        catch (Exception e)
        {
            Log.Warn("Failed to dispose GoldenLayout workspace", e);
        }
    }

    private async ValueTask SetLayoutLoading(bool value)
    {
        if (isLayoutLoading == value)
        {
            return;
        }

        isLayoutLoading = value;
        await InvokeAsync(StateHasChanged);
        await Task.Yield();
    }

    private static async Task<T> WithLayoutTimeout<T>(Task<T> task, string operation)
    {
        try
        {
            return await task.WaitAsync(LayoutJsTimeout);
        }
        catch (TimeoutException e)
        {
            throw new TimeoutException($"Timed out while trying to {operation}", e);
        }
    }

    private static async Task WithLayoutTimeout(Task task, string operation)
    {
        try
        {
            await task.WaitAsync(LayoutJsTimeout);
        }
        catch (TimeoutException e)
        {
            throw new TimeoutException($"Timed out while trying to {operation}", e);
        }
    }
}
