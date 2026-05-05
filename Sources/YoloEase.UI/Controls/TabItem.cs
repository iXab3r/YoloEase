using PoeShared.Common;

namespace YoloEase.UI.Controls;

/// <summary>
/// Represents one logical tab shown in the main GoldenLayout workspace.
/// </summary>
public class TabItem : DisposableReactiveObject
{
    private static readonly Binder<TabItem> Binder = new();

    static TabItem()
    {
        Binder.BindIf(x => x.DataContext is ICanBeSelected, x => x.IsSelected)
            .To(x => ((ICanBeSelected)x.DataContext).IsSelected);
    }

    public TabItem()
    {
        Id = Guid.NewGuid().ToString();
        Binder.Attach(this).AddTo(Anchors);
    }
    
    public string Id { get; }
    
    public string Title { get; set; }

    public Type? ViewType { get; set; }

    public int SortOrder { get; set; }
    
    public bool IsSelected { get; set; }
    
    public object DataContext { get; set; }

    public bool IsVisible { get; set; } = true;
}
