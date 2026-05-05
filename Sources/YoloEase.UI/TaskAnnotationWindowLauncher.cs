using System.Windows;
using System.Windows.Threading;
using PoeShared.Blazor.Wpf;
using PoeShared.Services;
using YoloEase.UI.Core;

namespace YoloEase.UI;

/// <summary>
/// Opens task annotation views in standalone Blazor windows.
/// </summary>
public static class TaskAnnotationWindowLauncher
{
    public static async Task Open(
        YoloEaseProject project,
        AnnotationTaskInfo task,
        IBlazorWindowAccessor blazorWindowAccessor,
        IFactory<IBlazorWindow, Dispatcher> blazorWindowFactory)
    {
        if (project.RemoteProject.Mode == AnnotationBackendMode.Offline)
        {
            await project.RemoteProject.NavigateToTask(task.Id);
        }

        var owner = blazorWindowAccessor.Window;
        var taskWindow = blazorWindowFactory.Create(owner.Dispatcher);
        taskWindow.Title = $"{task.Name} | YoloEase";
        taskWindow.ViewType = typeof(TaskAnnotationWindow);
        taskWindow.DataContext = new TaskWindowContext(project, task);
        taskWindow.TitleBarDisplayMode = TitleBarDisplayMode.System;
        taskWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        taskWindow.OwnerHandle = owner.GetWindowHandle();
        taskWindow.Width = 1280;
        taskWindow.Height = 760;
        taskWindow.MinWidth = 960;
        taskWindow.MinHeight = 600;
        taskWindow.ResizeMode = ResizeMode.CanResize;
        taskWindow.Padding = new Thickness(0);
        taskWindow.BorderThickness = new Thickness(1);
        taskWindow.AdditionalFiles = owner.AdditionalFiles;
        taskWindow.AdditionalFileProvider = owner.AdditionalFileProvider;
        taskWindow.Show();
    }
}
