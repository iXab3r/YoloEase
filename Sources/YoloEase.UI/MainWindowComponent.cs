using AntDesign;
using Microsoft.AspNetCore.Components;
using YoloEase.UI.Core;

namespace YoloEase.UI;

public abstract class YoloEaseComponent<T> : PoeShared.Blazor.BlazorReactiveComponent<T> where T : RefreshableReactiveObject
{
}

public partial class MainWindowComponent : YoloEaseComponent<MainWindowViewModel>
{
    public MainWindowComponent()
    {
    }
}