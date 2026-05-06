using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Threading;
using PoeShared.Common;
using PoeShared.Blazor.Wpf;
using PoeShared.Logging;
using PoeShared.Services;
using YoloEase.UI.Core;

namespace YoloEase.UI.TaskAnnotation;

/// <summary>
/// Opens task annotation views in standalone Blazor windows.
/// </summary>
public static class TaskAnnotationWindowLauncher
{
    private static readonly IFluentLog Log = typeof(TaskAnnotationWindowLauncher).PrepareLogger();

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
        taskWindow.Width = 1380;
        taskWindow.Height = 860;
        taskWindow.MinWidth = 960;
        taskWindow.MinHeight = 600;
        taskWindow.ResizeMode = ResizeMode.CanResize;
        taskWindow.Padding = new Thickness(0);
        taskWindow.BorderThickness = new Thickness(1);
        taskWindow.AdditionalFiles = owner.AdditionalFiles;
        taskWindow.AdditionalFileProvider = owner.AdditionalFileProvider;
        Disposable
            .Create(() =>
            {
                try
                {
                    if (owner.Dispatcher.CheckAccess())
                    {
                        taskWindow.Close();
                    }
                    else
                    {
                        owner.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            try
                            {
                                taskWindow.Close();
                            }
                            catch (Exception e)
                            {
                                Log.Warn("Failed to close task annotation window during project disposal", e);
                            }
                        }));
                    }
                }
                catch (Exception e)
                {
                    Log.Warn("Failed to queue task annotation window close during project disposal", e);
                }
            })
            .AddTo(project.Anchors);
        taskWindow.Show();
    }
}
