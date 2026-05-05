using System.Windows;
using System.Windows.Threading;
using PoeShared.Blazor.Wpf;
using PoeShared.Services;
using YoloEase.UI.Core;

namespace YoloEase.UI;

/// <summary>
/// Opens video frame extraction in a standalone, non-modal Blazor window.
/// </summary>
public static class VideoSplitterWindowLauncher
{
    /// <summary>
    /// Shows a standalone extraction window for the specified video without blocking the owner window.
    /// </summary>
    public static Task Open(
        YoloEaseProject project,
        FileInfo videoFile,
        IBlazorWindowAccessor blazorWindowAccessor,
        IFactory<IBlazorWindow, Dispatcher> blazorWindowFactory)
    {
        var owner = blazorWindowAccessor.Window;
        var splitterWindow = blazorWindowFactory.Create(owner.Dispatcher);
        splitterWindow.Title = $"{videoFile.Name} | Extract frames";
        splitterWindow.ViewType = typeof(VideoSplitterDialog);
        splitterWindow.DataContext = new VideoSplitterWindowContext(project, videoFile);
        splitterWindow.TitleBarDisplayMode = TitleBarDisplayMode.System;
        splitterWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        splitterWindow.OwnerHandle = owner.GetWindowHandle();
        splitterWindow.Width = 720;
        splitterWindow.Height = 520;
        splitterWindow.MinWidth = 560;
        splitterWindow.MinHeight = 420;
        splitterWindow.ResizeMode = ResizeMode.CanResize;
        splitterWindow.Padding = new Thickness(0);
        splitterWindow.BorderThickness = new Thickness(1);
        splitterWindow.AdditionalFiles = owner.AdditionalFiles;
        splitterWindow.AdditionalFileProvider = owner.AdditionalFileProvider;
        splitterWindow.Show();
        return Task.CompletedTask;
    }
}
