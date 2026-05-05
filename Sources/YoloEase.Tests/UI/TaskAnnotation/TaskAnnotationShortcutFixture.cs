using Microsoft.AspNetCore.Components.Web;
using Shouldly;
using YoloEase.UI.TaskAnnotation;

namespace YoloEase.Tests.UI.TaskAnnotation;

public class TaskAnnotationShortcutFixture
{
    /// <summary>
    /// WHAT: Verifies the task editor treats Q as an alternate delete shortcut.
    /// HOW: Sends representative keyboard events to the shortcut predicate.
    /// </summary>
    [Test]
    public void ShouldTreatQAsDeleteShortcut()
    {
        // Given / When / Then
        TaskAnnotationWindow.IsDeleteShortcut(new KeyboardEventArgs { Key = "q", Code = "KeyQ" }).ShouldBeTrue();
        TaskAnnotationWindow.IsDeleteShortcut(new KeyboardEventArgs { Key = "Delete" }).ShouldBeTrue();
        TaskAnnotationWindow.IsDeleteShortcut(new KeyboardEventArgs { Key = "Backspace" }).ShouldBeTrue();
    }

    /// <summary>
    /// WHAT: Verifies modified Q remains available for other application/window shortcuts.
    /// HOW: Checks that Ctrl, Meta, and Alt modified Q events do not count as editor delete.
    /// </summary>
    [Test]
    public void ShouldIgnoreModifiedQAsDeleteShortcut()
    {
        // Given / When / Then
        TaskAnnotationWindow.IsDeleteShortcut(new KeyboardEventArgs { Key = "q", Code = "KeyQ", CtrlKey = true }).ShouldBeFalse();
        TaskAnnotationWindow.IsDeleteShortcut(new KeyboardEventArgs { Key = "q", Code = "KeyQ", MetaKey = true }).ShouldBeFalse();
        TaskAnnotationWindow.IsDeleteShortcut(new KeyboardEventArgs { Key = "q", Code = "KeyQ", AltKey = true }).ShouldBeFalse();
    }
}
