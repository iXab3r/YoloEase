using System.Linq;
using System.Reactive.Disposables;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PoeShared.Blazor.Controls.GoldenLayout;
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
    private bool isInitializingLayout;
    private bool isLayoutLoading;
    private bool isSyncingLayout;
    private bool isPreparingLayoutTabs;

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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (DataContext?.YoloEaseProject == null)
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
            await DisposeGoldenLayout();

            currentLayoutContext = DataContext;
            layoutSubscriptions = new CompositeDisposable();
            goldenLayout = await GoldenLayoutInterop.Create(ElementRef);
            goldenLayoutBlazorAdapter = Services.GetRequiredService<IGoldenLayoutBlazorAdapter>();
            await goldenLayoutBlazorAdapter.Initialize(goldenLayout);
            await goldenLayout.LoadLayout(LayoutMain);

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

        isSyncingLayout = true;
        await SetLayoutLoading(true);
        try
        {
            var adapter = goldenLayoutBlazorAdapter;
            var visibleTabs = DataContext.Tabs.Items
                .Where(x => x.IsVisible && x.DataContext != null)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Title)
                .ToArray();
            var visibleTabIds = visibleTabs.Select(x => x.Id).ToHashSet();

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

                    await adapter.AddChild(
                        MainPanelId,
                        new GLBlazorComponentState
                        {
                            Id = tab.Id,
                            Title = tab.Title,
                            Closeable = false,
                            ReorderEnabled = true,
                        },
                        dataContext: tab.DataContext,
                        bodyViewType: tab.ViewType);

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
        finally
        {
            isSyncingLayout = false;
            await SetLayoutLoading(false);
        }
    }

    private async ValueTask RemoveGoldenLayoutTab(string tabId)
    {
        try
        {
            if (goldenLayoutBlazorAdapter != null)
            {
                await goldenLayoutBlazorAdapter.Remove(tabId);
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

        await goldenLayout.FocusById(tabId);
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
        await DisposeGoldenLayout();
        await base.DisposeAsync();
    }

    private async ValueTask DisposeGoldenLayout()
    {
        layoutSubscriptions?.Dispose();
        layoutSubscriptions = null;

        registeredTabContexts.Clear();
        registeredTabTitles.Clear();
        goldenLayoutBlazorAdapter?.Dispose();
        goldenLayoutBlazorAdapter = null;

        if (goldenLayout == null)
        {
            currentLayoutContext = null;
            return;
        }

        var layoutToDispose = goldenLayout;
        goldenLayout = null;
        currentLayoutContext = null;
        await layoutToDispose.DisposeAsync();
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
}
