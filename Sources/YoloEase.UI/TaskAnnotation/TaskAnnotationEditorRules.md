# Task Annotation Editor Rules

These rules describe the intended behavior of `TaskAnnotation/TaskAnnotationWindow.razor`.
Update this file whenever editor interaction rules change.

The window shell should stay small: render chrome is split into `TaskAnnotationToolbar`, `TaskAnnotationCanvas`,
`TaskAnnotationInspector`, and `TaskAnnotationBottomBar`, all driven through `ITaskAnnotationWindowContext`.

## Shape Activation

- A hovered shape is treated as active in the same way as a selected shape.
- Keyboard and command operations use the effective shape set: selected shapes plus the hovered shape when it is not already selected.
- Code should use `EffectiveShapes` / `EffectiveShape` for current and future keyboard or command operations.
- Direct mouse drag operations snapshot the target shape set at drag start so move, resize, and rotate have a stable operation target and one undo step.
- `Ctrl+click` toggles a shape in the selected group.
- `Ctrl+A` selects every shape on the current frame.
- When multiple shapes are selected, move, resize, rotate, label-change, delete, cut, copy, and paste target the group.

## Shortcuts

- `N` starts rectangle creation when labels exist.
- `Ctrl+1` through `Ctrl+9` select a label by visible label index.
- `Ctrl+N` cycles to the next visible label.
- `Alt+1` through `Alt+9` run the corresponding auto-annotation model on the current frame.
- `Shift+Alt+1` through `Shift+Alt+9` run the corresponding auto-annotation model on all task frames.
- Label shortcuts apply to `EffectiveShapes` when any exist; otherwise they change the active label for the next shape.
- `Ctrl+C`, `Ctrl+X`, and `Ctrl+V` copy, cut, and paste the effective shape set.
- `Delete` / `Backspace` delete `EffectiveShapes`.
- `Escape` cancels the current tool or edit operation and clears selection.
- `D` and `F` move one frame backward/forward.
- `C` and `V` move by the configured frame step.

## Rectangle Creation And Editing

- Rectangle is the only implemented shape kind for now.
- Rectangle creation is a two-click flow: first click anchors, pointer move previews, second click commits.
- Minimum committed shape size is 4 image pixels.
- The left toolbar rectangle tool uses a local CSS glyph, so it is not dependent on icon-font coverage.
- LMB drag on empty canvas pans by default.
- Mouse wheel zooms in place around the cursor.
- Hover and selection both show resize and rotate handles.
- Left-click selection should trigger a short, subtle fill/background flash on the selected shape. It must not change geometry, scale, outline offset, or layout.
- Shift during resize preserves the original rectangle aspect ratio.
- Shift during rotate snaps rotation to 15 degree increments.
- Group resize scales selected shapes around the combined selection bounds.
- Group rotate rotates selected shapes around the combined selection center.

## Clipboard

- Clipboard is task-window local and stores shape geometry, labels, rotation, and source.
- Cut removes the effective shape set through normal undoable editor history.
- Paste starts an ephemeral preview mode. Preview shapes follow the mouse in image coordinates.
- A left click commits the preview shapes to the current frame, assigns new shape IDs, selects the pasted group, and records one undo step.
- `Escape` cancels the paste preview without changing annotations.

## Rendering

- Shape fill and border use the label color.
- Label name and size are drawn outside the shape with black outline text and no label background.
- Label name and size are only visible for the active shape: hovered or selected.
- Shape text and edit knobs use an enlarged base size and scale down more slowly than the image when zooming out.
- Shape handles use visible black outline on hover.
- The editor layout must keep the main canvas and bottom bar in separate CSS grid rows; only inspector label and shape lists should scroll.
- The bottom bar shows image size plus cursor and active selection coordinates.
- The bottom bar should stay slim enough that it does not visually compete with the canvas.
- Task editor layout follows a GoldenLayout-style split: left main editor pane, right tabbed inspector with Shapes, Labels, and Models tabs. Do not reintroduce the Issues tab.
- The Shapes tab may filter the list by all/manual/model-generated sources, and model-generated shapes should remain visibly tagged by source.

## Auto-Annotation

- Auto-annotation runs are launched from the editor Models tab, toolbar menu, or `Alt+1..9` shortcuts.
- ONNX/YoloDotNet engine initialization touches native runtime libraries and is unsafe by default. Never auto-load, pre-load, validate, warm up, or run models when the app starts, a project loads, the task window opens, the Models tab renders, settings change, autosave runs, or background refresh executes.
- Model validation and inference require explicit user action: `Check model`, `Run current`, `Run all`, toolbar/menu commands, or the documented auto-annotation shortcuts.
- Broken or incompatible models must stay visible with clear status/error text and must not prevent the editor or project from opening.
- V1 runs one explicit model entry at a time. Do not add a run-all-enabled pipeline command without a product decision.
- Custom ONNX models are copied into project storage under `models/auto-annotation/<sha256>/`.
- `Latest` model entries resolve to the newest trained ONNX model at run time and should show the resolved file before or after validation.
- Generated shapes use `Source = automatic:<modelEntryId>`.
- Rerunning a model replaces only shapes from the same model entry and frame after that frame succeeds.
- Manual edits to model-generated shapes convert them back to `Source = manual`.
- Enabled model labels must resolve to project labels before a run. Disabled labels may stay unmapped and are skipped.
- Autosave and editing mutations are paused during auto-annotation; one undo step should restore the pre-run state for completed frame merges.

## Persistence

- Saved annotations include `Kind`, `BoundingBox`, `LabelId`, `FrameIndex`, `RotationDegrees`, and `Source`.
- Prediction confidence and resolved model hash are session/run-summary data in v1; they are not persisted into the CVAT XML shape schema.
- Offline task annotations use the CVAT XML shape format in `assets/training/annotations.project.{projectId}.task.{taskId}.xml`.
- Legacy offline JSON annotation state may be read as a fallback, but new editor saves should write CVAT XML.
- Offline CVAT XML and remote CVAT upload should preserve rotation for rectangle shapes.
- Offline task frame images should resolve through `AnnotationProjectAccessor.ResolveTaskFrameFile` before falling back to the local assets cache.
- The task editor autosaves dirty offline annotation edits periodically and before closing/removing frames.
- Removing an image from a task is a non-undoable dataset operation. It must use a two-step button, remove the frame annotations, reindex later frames, and delete the image file when it is not referenced by another task.
