# AGENTS.md

YoloEase.UI-specific guidance for coding agents.

## Build Output Rule

- Never create an `artifacts` folder under `Sources/` or any subfolder.
- Do not use `Sources/**/artifacts` for build, restore, publish, scratch, or test output.
- If a build recreates `Sources/YoloEase.UI/bin/Debug/artifacts`, remove it after verifying the resolved absolute path is exactly that directory.
- Prefer temporary build output outside the repository, for example:
  `dotnet build Sources\YoloEase.UI\YoloEase.UI.csproj --no-restore -p:BaseOutputPath="$env:TEMP\YoloEaseBuild\bin\"`

## Agent Scratch And Temp Files

- Do not place disposable agent files inside `Sources/YoloEase.UI` or elsewhere in the repository. Temporary scripts, screenshots, traces, extracted archives, diagnostic outputs, package caches, model probes, and experimental build outputs must go under an external temp directory such as `$env:TEMP\YoloEaseCodex\...`.
- Before running UI diagnostics, Playwright, model probes, restore/build commands with custom paths, or any script that writes files, explicitly choose an output/cache directory outside `D:\Work\YoloEase`.
- If a tool recreates repo-local disposable output anyway, verify the absolute path and remove only that disposable output before finishing.

## UI Architecture Notes

- Main tabs are hosted through GoldenLayout; each tab content area is expected to be padded by the tab wrapper.
- Task annotation runs in a separate `IBlazorWindow` using `TaskAnnotationWindow.razor`; launch it through `TaskAnnotationWindowLauncher` so task lists and trainer actions share one window setup.
- Refreshable UI/project services inherit centralized coalescing refresh behavior from `RefreshableReactiveObject`; do not add per-call refresh locks or busy guards unless a non-refresh operation needs its own latch.
- Treat filesystem, dialogs, process launching, network calls, clipboard, drag/drop, browser/webview interop, and other OS or external-system interactions as unsafe by default. These operations must be guarded at the UI/service boundary, logged with context, and reported to the user without crashing the app.
- Any scheduler callback, reactive subscription, dispatcher work item, background task, or event handler that performs external IO/network/process/dialog work must have a local `try/catch` around the operation body.
- Main shell and trainer bindings must tolerate `YoloEaseProject` becoming `null` at any time, including while prerequisite checks, refreshes, scheduler callbacks, or trainer work are active. Avoid null-forgiving project chains in `Binder` and `WhenAnyValue`; switch from a nullable project stream to project-scoped observables instead.
- Project close must detach UI/data contexts and dispose project-backed component bindings before `YoloEaseProject` is nulled. Use the shell attach/detach gate rather than relying on components to survive a half-null project graph.
- Retired project instances should be disposed defensively after in-flight callbacks have had time to unwind. Do not aggressively dispose project objects directly inside property-change handlers while scheduler, trainer, refresh, or editor callbacks may still reference them.
- Offline image frames are stored under the project storage `assets/training` directory and should be resolved by `AnnotationProjectAccessor.ResolveTaskFrameFile`.
- Offline annotation persistence uses CVAT-style XML under `assets/training/annotations.project.{projectId}.task.{taskId}.xml`; keep legacy JSON as read-only fallback only.
- Annotation persistence currently supports rectangle shapes with label, frame, bounds, rotation, source, and shape kind metadata.

## Task Annotation Editor

- Read and update `TaskAnnotationEditorRules.md` whenever editor behavior changes.
- Task annotation services should rely on Fody/reactive properties plus `SourceList`/`SourceCache` change streams for change notification. Do not add manual `Touch`/settings-changed subjects for persistence triggers when reactive properties or reactive collections can represent the same state.
- Any operation launched through `Task.Run`, scheduler callbacks, background loops, or other separate-thread execution must catch and log/report non-cancellation exceptions at the async boundary. Cancellation may flow normally, but unexpected failures must not disappear into background tasks.
- ML model loading/inference uses native runtime libraries and is unsafe by default. Never pre-load, validate, warm up, or run ONNX/YoloDotNet engines on app startup, project load, window open, tab render, refresh, autosave, or reactive settings changes; require an explicit user action such as `Check model`, `Run current`, `Run all`, or the auto-annotation shortcuts.
- Keep safe metadata inspection separate from native engine initialization. A broken model must remain visible with a clear status, and must never prevent the task editor or project from opening.
- Hovered shapes are active like selected shapes; command routing should use the effective shape set (`EffectiveShapes`) so group selection and hover-selection stay consistent.
- Group selection is current-frame scoped. `Ctrl+click` toggles one shape, `Ctrl+A` selects all current-frame shapes, and bulk move/resize/rotate/delete/label-change must remain undoable as one operation.
- Clipboard operations are task-window local. `Ctrl+C`, `Ctrl+X`, and `Ctrl+V` plus toolbar buttons should operate on the effective shape set; paste must remain an ephemeral mouse-following preview until left-click commit.
- Offline editor edits should autosave periodically to CVAT XML and save before close or frame removal.
- Removing an image from the task/dataset is not an editor undo operation; use a full-size two-step remove/cancel control and route through `AnnotationProjectAccessor.RemoveTaskFrame`.
- Keep editor interactions compact and CVAT-like: toolbar left, canvas center, inspector right, bottom action bar fixed.
- Keep the task editor in a GoldenLayout-style split: main editor pane on the left, right side as a vertical Shapes/Labels stack. Do not add an Issues tab until it has real content.
- Keep only inspector shape/label lists scrollable; do not reintroduce main-window scrolling in task editor windows.
- Keep bottom-bar telemetry useful but terse: image size, cursor position, and active shape position/size.
- Selection flash should change only fill/background, never geometry, scale, outline offset, or layout.

## Styling Notes

- Preserve the compact dark workbench visual style.
- Prefer Bootstrap-compatible controls and local CSS over adding more AntBlazor chrome.
- For row actions, keep primary/navigation actions such as `Open` larger and more prominent than destructive actions such as `Delete` or `Remove`; separate mixed actions with clear spacing instead of attached Bootstrap button groups.
- Compact row-level two-step confirmation states must use icon-only confirm/cancel buttons with tooltips so they fit without crowding.
- Labels should be pill-like, compact, and color-led.
- Shape labels and sizes render outside active annotations only, with outline text and no boxed backgrounds.
- Use subtle selection motion only when it improves object focus; avoid decorative animation in the work surface.
- Main GoldenLayout tabs should use `TabPageLayout`: a fixed-height top bar first, then a single body region with scrollbar support. Keep top-bar labels on one line with main label and sublabel sequentially instead of stacked rows.

## Destructive Actions

- Non-undoable destructive UI actions must use two-step confirmation instead of immediate execution.
- Full-size two-step buttons replace the initial destructive button with confirm and cancel buttons; confirm appears first and keeps the destructive label, cancel is immediately to its right.
- Small two-step buttons use icons only: the cancel icon occupies the original button position, and the confirm icon appears directly to its left.
- Two-step controls must reserve the final confirm/cancel width before confirmation; use a fixed-width action slot or an equivalent stable layout so switching states never resizes table columns, bars, or neighboring controls.
- Undoable editor operations, such as annotation shape deletion inside the task editor history, may remain immediate.
