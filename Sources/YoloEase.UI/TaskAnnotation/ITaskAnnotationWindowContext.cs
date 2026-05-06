using AntDesign;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TaskAnnotation;

public interface ITaskAnnotationWindowContext
{
    ElementReference EditorViewport { get; set; }

    ElementReference EditorHitLayer { get; set; }

    AnnotationTaskInfo TaskInfo { get; }

    IReadOnlyList<TaskAnnotationWindow.EditorHandle> ResizeHandles { get; }

    IReadOnlyList<TaskAnnotationWindow.EditorShape> CurrentFrameShapes { get; }

    IReadOnlyList<TaskAnnotationWindow.EditorShape> ListedCurrentFrameShapes { get; }

    IReadOnlyList<TaskAnnotationWindow.EditorShape> PendingPasteShapes { get; }

    IReadOnlyList<AutoAnnotationSuggestion> CurrentFrameSuggestions { get; }

    IReadOnlyList<AnnotationLabelInfo> OrderedLabels { get; }

    IReadOnlyList<AutoAnnotationModelConfig> AutoAnnotationModels { get; }

    AutoAnnotationModelConfig? FirstRunnableModel { get; }

    TaskAnnotationWindow.TaskEditorInspectorTab ActiveInspectorTab { get; set; }

    TaskAnnotationWindow.TaskEditorSourceFilter ShapeSourceFilter { get; set; }

    TaskAnnotationWindow.EditorRect? DraftRectangle { get; }

    TaskAnnotationWindow.EditorRect? SelectionMarquee { get; }

    IReadOnlyList<TaskAnnotationWindow.EditorPoint> FreeformSelectionPoints { get; }

    bool HasLabels { get; }

    bool ShouldShowCreationGuide { get; }

    bool ShouldShowSelectionBounds { get; }

    bool CanCopySelection { get; }

    bool CanDeleteSelection { get; }

    bool CanPasteClipboard { get; }

    bool CanCopyPreviousFrameAnnotations { get; }

    bool CanUndo { get; }

    bool CanRedo { get; }

    bool CanRunAutoAnnotation { get; }

    bool IsAutoAnnotating { get; }

    bool IsModelOperationActive { get; }

    bool IsDirty { get; }

    bool IsAutoSaving { get; }

    bool IsCanvasInteractionBlocked { get; }

    bool CanRemoveCurrentFrame { get; }

    bool ShowSuggestions { get; }

    bool ShowAnnotations { get; }

    int EffectiveFrameIndex { get; }

    int PendingRemoveFrameIndex { get; }

    int ObjectCount { get; }

    int SuggestionCount { get; }

    int CurrentFrameSuggestionCount { get; }

    int ZoomPercent { get; }

    double ViewOffsetX { get; }

    double ViewOffsetY { get; }

    DateTimeOffset? LastSavedAt { get; }

    string? CurrentImageSourceUrl { get; }

    bool ShouldShowFrameNavigationFlash { get; }

    int FrameNavigationFlashVersion { get; }

    string FrameNavigationFlashClass { get; }

    string CurrentFrameLabel { get; }

    string CurrentFileLabel { get; }

    string ImageLayerStyle { get; }

    string ImageSizeStatus { get; }

    string CursorPositionStatus { get; }

    string SelectionPositionStatus { get; }

    string AutoAnnotationProgressLabel { get; }

    string? AutoAnnotationStatusText { get; }

    string ModelOperationProgressText { get; }

    string ModelOperationProgressPercentLabel { get; }

    string? PendingRemoveModelId { get; }

    string GetToolClass(TaskAnnotationWindow.EditorTool tool);

    string GetFrameNavigationButtonClass(TaskAnnotationWindow.FrameNavigationAction action);

    string GetSuggestionsToolClass();

    string GetAnnotationsToolClass();

    string GetInspectorTabClass(TaskAnnotationWindow.TaskEditorInspectorTab tab);

    string GetModelOperationProgressClass();

    string GetModelOperationProgressBarStyle();

    string GetSourceFilterClass(TaskAnnotationWindow.TaskEditorSourceFilter filter);

    string GetShapeSourceClass(TaskAnnotationWindow.EditorShape shape);

    string GetModelStatusClass(AutoAnnotationModelConfig model);

    string GetMappingRowClass(AutoAnnotationLabelMapping mapping);

    string GetHitLayerClass();

    string GetShapeClass(TaskAnnotationWindow.EditorShape shape);

    string GetPasteShapeClass(TaskAnnotationWindow.EditorShape shape);

    string GetSuggestionClass(AutoAnnotationSuggestion suggestion);

    string GetObjectListItemClass(TaskAnnotationWindow.EditorShape shape);

    string GetLabelPillClass(AnnotationLabelInfo label);

    string GetShapeStyle(TaskAnnotationWindow.EditorShape shape, AnnotationLabelInfo label);

    string GetSuggestionStyle(AutoAnnotationSuggestion suggestion, AnnotationLabelInfo label);

    string GetDraftShapeStyle();

    string GetSelectionMarqueeStyle();

    string GetFreeformSelectionPolylinePoints();

    string GetVerticalGuideStyle();

    string GetHorizontalGuideStyle();

    string GetGuideCrossStyle();

    string GetRotateHandleClass(TaskAnnotationWindow.EditorShape shape);

    string GetHandleClass(TaskAnnotationWindow.EditorShape shape, TaskAnnotationWindow.EditorHandle handle);

    string GetSelectionBoundsStyle();

    string GetSelectionRotateHandleClass();

    string GetSelectionHandleClass(TaskAnnotationWindow.EditorHandle handle);

    AnnotationLabelInfo GetLabel(int labelId);

    bool ShouldShowShapeDetails(TaskAnnotationWindow.EditorShape shape);

    bool ShouldShowShapeHandles(TaskAnnotationWindow.EditorShape shape);

    bool CanModifySuggestions { get; }

    bool CanRunModel(AutoAnnotationModelConfig model);

    bool ShouldShowModelLoadButton(AutoAnnotationModelConfig model);

    int GetModelSuggestionCount(AutoAnnotationModelConfig model);

    void SelectTool(TaskAnnotationWindow.EditorTool tool);

    void StartRectangleTool();

    void CutSelection();

    void CopySelection();

    void DeleteSelection();

    void BeginPaste();

    void CopyPreviousFrameAnnotations();

    void Undo();

    void Redo();

    void ResetZoom();

    void ToggleShowSuggestions();

    void ToggleShowAnnotations();

    void GoFirst();

    void GoPreviousStep();

    void GoPrevious();

    void GoNext();

    void GoNextStep();

    void GoLast();

    void HandleCanvasMouseDown(MouseEventArgs args);

    void HandleCanvasMouseMove(MouseEventArgs args);

    void HandleCanvasMouseUp(MouseEventArgs args);

    void HandleCanvasMouseLeave(MouseEventArgs args);

    void HandleCanvasWheel(WheelEventArgs args);

    void SuppressBrowserDrag(DragEventArgs args);

    void SelectShapeFromList(string shapeId, MouseEventArgs args);

    void SetHoveredShape(string shapeId);

    void ClearHoveredShape(string shapeId);

    void SetHoveredSuggestion(string suggestionId);

    void ClearHoveredSuggestion(string suggestionId);

    void SelectLabel(int labelId);

    void AddLatestAutoAnnotationModel();

    Task ImportAutoAnnotationModel();

    void DuplicateAutoAnnotationModel(AutoAnnotationModelConfig model);

    void BeginRemoveAutoAnnotationModel(AutoAnnotationModelConfig model);

    void CancelRemoveAutoAnnotationModel();

    void RemoveAutoAnnotationModel(AutoAnnotationModelConfig model);

    void MoveAutoAnnotationModel(AutoAnnotationModelConfig model, int delta);

    Task ValidateAutoAnnotationModel(AutoAnnotationModelConfig model);

    Task RunFirstAutoAnnotation(bool allFrames);

    Task RunAutoAnnotation(AutoAnnotationModelConfig model, bool allFrames);

    void CancelAutoAnnotationRun();

    void ClearModelSuggestions(AutoAnnotationModelConfig model);

    void SetModelEnabled(AutoAnnotationModelConfig model, ChangeEventArgs args);

    void SetModelCreateSuggestions(AutoAnnotationModelConfig model, ChangeEventArgs args);

    void UpdateModelConfidence(AutoAnnotationModelConfig model, ChangeEventArgs args);

    void UpdateModelIoU(AutoAnnotationModelConfig model, ChangeEventArgs args);

    void SetAllModelMappingsEnabled(AutoAnnotationModelConfig model, bool isEnabled);

    void SetMappingEnabled(AutoAnnotationModelConfig model, AutoAnnotationLabelMapping mapping, ChangeEventArgs args);

    void SetMappingProjectLabel(AutoAnnotationModelConfig model, AutoAnnotationLabelMapping mapping, ChangeEventArgs args);

    void AcceptSuggestion(string suggestionId);

    void RemoveSuggestion(string suggestionId);

    void AcceptCurrentFrameSuggestions();

    void ClearCurrentFrameSuggestions();

    void BeginRemoveCurrentFrame();

    void CancelRemoveCurrentFrame();

    Task RemoveCurrentFrame();

    Task FinishJob();

    Task CloseWindow();

    string FormatShapeSource(TaskAnnotationWindow.EditorShape shape);

    string FormatSuggestionSource(AutoAnnotationSuggestion suggestion);

    string FormatSuggestionConfidence(AutoAnnotationSuggestion suggestion);

    string FormatModelStatus(AutoAnnotationModelConfig model);

    string FormatResolvedModel(AutoAnnotationModelConfig model);

    string FormatModelHeaderName(AutoAnnotationModelConfig model);

    string FormatModelShortcut(int modelIndex);

    string ModelMappingHelpText { get; }

    string FormatRect(TaskAnnotationWindow.EditorShape shape);

    string FormatRect(TaskAnnotationWindow.EditorRect rectangle);

    string FormatShapeLabel(AnnotationLabelInfo label);

    string FormatStatus(AnnotationTaskStatus status);

    string GetStatusClass(AnnotationTaskStatus status);

    string GetHandleStyle(TaskAnnotationWindow.EditorHandle handle);

    string GetLabelShortcutText(int labelIndex);
}
