using System.ComponentModel;
using System.Reactive;
using System.Windows.Input;
using PoeShared.Blazor.Wpf;
using PoeShared.Native;
using PoeShared.Scaffolding;

namespace YoloEase.UI.Scaffolding;

internal sealed class BlazorWindowViewController : DisposableReactiveObject, IWindowViewController
{
    private readonly IBlazorWindow blazorWindow;

    public BlazorWindowViewController(IBlazorWindow blazorWindow)
    {
        this.blazorWindow = blazorWindow;
    }

    public void Hide()
    {
        blazorWindow.Hide();
    }

    public void Show()
    {
        blazorWindow.Show();
    }

    public IObservable<Unit> WhenLoaded => blazorWindow.WhenLoaded.ToUnit();

    public IObservable<Unit> WhenUnloaded => blazorWindow.WhenUnloaded.ToUnit();

    public IObservable<Unit> WhenDeactivated => blazorWindow.WhenDeactivated.ToUnit();

    public IObservable<Unit> WhenActivated => blazorWindow.WhenActivated.ToUnit();

    public IObservable<Unit> WhenClosed => blazorWindow.WhenClosed.ToUnit();

    public IObservable<CancelEventArgs> WhenClosing => blazorWindow.WhenClosing;

    public IObservable<Unit> WhenRendered => blazorWindow.WhenLoaded.ToUnit();

    public IObservable<KeyEventArgs> WhenKeyUp => blazorWindow.WhenKeyUp;

    public IObservable<KeyEventArgs> WhenKeyDown => blazorWindow.WhenKeyDown;

    public IObservable<KeyEventArgs> WhenPreviewKeyDown => blazorWindow.WhenPreviewKeyDown;

    public IObservable<KeyEventArgs> WhenPreviewKeyUp => blazorWindow.WhenPreviewKeyUp;

    public IntPtr Handle => blazorWindow is IBlazorWindowNativeController native ? native.GetWindowHandle() : IntPtr.Zero;

    public void TakeScreenshot(string fileName)
    {
    }

    public void Minimize()
    {
        blazorWindow.Minimize();
    }

    public void Activate()
    {
        blazorWindow.Activate();
    }

    public void Close(bool? result)
    {
        blazorWindow.Close();
    }

    public void Close()
    {
        blazorWindow.Close();
    }

    public void SetWindowRect(WinRect rect)
    {
        blazorWindow.SetWindowRect(rect);
    }

    public bool Topmost
    {
        get => blazorWindow.Topmost;
        set => blazorWindow.Topmost = value;
    }
}
