using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using AntDesign;
using DynamicData;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PoeShared.Blazor.Scaffolding;
using PoeShared.Blazor.Wpf;
using PoeShared.Logging;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;
using YoloEase.UI.Scaffolding;

namespace YoloEase.UI.TaskAnnotation;

public partial class TaskAnnotationWindow
{
    private const int FrameStep = 10;
    private const string ModelMappingHelpText =
        "Model label mapping controls how detections from this ONNX model become project labels. Each row is one label reported by the model. Enable rows you want to import, disable rows you want to ignore, and choose the project label that should receive those detections. Enabled rows without a project label block runs so detections are not saved under the wrong class. The same ONNX model can be added multiple times with different mappings and thresholds.";
    private const double ViewPadding = 32;
    private const double MinZoom = 0.05;
    private const double MaxZoom = 24;
    private const double WheelZoomFactor = 1.12;
    private const double MinShapeSize = 4;
    private const double HitTolerancePixels = 8;
    private const double FreeformSelectionPointSpacingPixels = 4;
    private const double RotateHandleOffset = 28;
    private const double RotationSnapDegrees = 15;
    private const int ImagePreloadRadius = 2;
    private const string JsModulePath = "./assets/js/taskAnnotationEditor.js";
    private static readonly TimeSpan AutoSaveInterval = TimeSpan.FromSeconds(5);
    private static readonly IFluentLog Log = typeof(TaskAnnotationWindow).PrepareLogger();

    private static readonly EditorHandle[] ResizeHandles =
    {
        EditorHandle.NorthWest,
        EditorHandle.North,
        EditorHandle.NorthEast,
        EditorHandle.East,
        EditorHandle.SouthEast,
        EditorHandle.South,
        EditorHandle.SouthWest,
        EditorHandle.West,
    };

    ElementReference ITaskAnnotationWindowContext.EditorViewport
    {
        get => editorViewport;
        set => editorViewport = value;
    }

    ElementReference ITaskAnnotationWindowContext.EditorHitLayer
    {
        get => editorHitLayer;
        set => editorHitLayer = value;
    }

    AnnotationTaskInfo ITaskAnnotationWindowContext.TaskInfo => TaskInfo;

    IReadOnlyList<EditorHandle> ITaskAnnotationWindowContext.ResizeHandles => TaskAnnotationWindow.ResizeHandles;

    IReadOnlyList<EditorShape> ITaskAnnotationWindowContext.CurrentFrameShapes => CurrentFrameShapes;

    IReadOnlyList<EditorShape> ITaskAnnotationWindowContext.ListedCurrentFrameShapes => ListedCurrentFrameShapes;

    IReadOnlyList<EditorShape> ITaskAnnotationWindowContext.PendingPasteShapes => PendingPasteShapes;

    IReadOnlyList<AutoAnnotationSuggestion> ITaskAnnotationWindowContext.CurrentFrameSuggestions => VisibleCurrentFrameSuggestions;

    IReadOnlyList<AnnotationLabelInfo> ITaskAnnotationWindowContext.OrderedLabels => OrderedLabels;

    IReadOnlyList<AutoAnnotationModelConfig> ITaskAnnotationWindowContext.AutoAnnotationModels => AutoAnnotationModels;

    AutoAnnotationModelConfig? ITaskAnnotationWindowContext.FirstRunnableModel => FirstRunnableModel;

    TaskEditorInspectorTab ITaskAnnotationWindowContext.ActiveInspectorTab
    {
        get => activeInspectorTab;
        set => activeInspectorTab = value;
    }

    TaskEditorSourceFilter ITaskAnnotationWindowContext.ShapeSourceFilter
    {
        get => shapeSourceFilter;
        set => shapeSourceFilter = value;
    }

    EditorRect? ITaskAnnotationWindowContext.DraftRectangle => draftRectangle;

    EditorRect? ITaskAnnotationWindowContext.SelectionMarquee => selectionMarquee;

    IReadOnlyList<EditorPoint> ITaskAnnotationWindowContext.FreeformSelectionPoints => freeformSelectionPoints;

    bool ITaskAnnotationWindowContext.HasLabels => HasLabels;

    bool ITaskAnnotationWindowContext.ShouldShowCreationGuide => ShouldShowCreationGuide;

    bool ITaskAnnotationWindowContext.ShouldShowSelectionBounds => ShouldShowSelectionBounds;

    bool ITaskAnnotationWindowContext.CanCopySelection => CanCopySelection;

    bool ITaskAnnotationWindowContext.CanDeleteSelection => CanDeleteSelection;

    bool ITaskAnnotationWindowContext.CanPasteClipboard => CanPasteClipboard;

    bool ITaskAnnotationWindowContext.CanCopyPreviousFrameAnnotations => CanCopyPreviousFrameAnnotations;

    bool ITaskAnnotationWindowContext.CanUndo => CanUndo;

    bool ITaskAnnotationWindowContext.CanRedo => CanRedo;

    bool ITaskAnnotationWindowContext.CanRunAutoAnnotation => CanRunAutoAnnotation;

    bool ITaskAnnotationWindowContext.IsAutoAnnotating => isAutoAnnotating;

    bool ITaskAnnotationWindowContext.IsModelOperationActive => IsModelOperationActive;

    bool ITaskAnnotationWindowContext.IsDirty => isDirty;

    bool ITaskAnnotationWindowContext.IsAutoSaving => isAutoSaving;

    bool ITaskAnnotationWindowContext.IsCanvasInteractionBlocked => IsCanvasInteractionBlocked;

    bool ITaskAnnotationWindowContext.CanRemoveCurrentFrame => CanRemoveCurrentFrame;

    bool ITaskAnnotationWindowContext.ShowSuggestions => showSuggestions;

    bool ITaskAnnotationWindowContext.ShowAnnotations => showAnnotations;

    int ITaskAnnotationWindowContext.EffectiveFrameIndex => EffectiveFrameIndex;

    int ITaskAnnotationWindowContext.PendingRemoveFrameIndex => pendingRemoveFrameIndex;

    int ITaskAnnotationWindowContext.ObjectCount => editorShapes.Count;

    int ITaskAnnotationWindowContext.SuggestionCount => autoAnnotationSuggestions.Count;

    int ITaskAnnotationWindowContext.CurrentFrameSuggestionCount => CurrentFrameSuggestionCount;

    int ITaskAnnotationWindowContext.ZoomPercent => ZoomPercent;

    double ITaskAnnotationWindowContext.ViewOffsetX => viewOffsetX;

    double ITaskAnnotationWindowContext.ViewOffsetY => viewOffsetY;

    DateTimeOffset? ITaskAnnotationWindowContext.LastSavedAt => lastSavedAt;

    string? ITaskAnnotationWindowContext.CurrentImageSourceUrl => currentImageSourceUrl;

    bool ITaskAnnotationWindowContext.ShouldShowFrameNavigationFlash => frameNavigationDirection != FrameNavigationDirection.None;

    int ITaskAnnotationWindowContext.FrameNavigationFlashVersion => frameNavigationFlashVersion;

    string ITaskAnnotationWindowContext.FrameNavigationFlashClass => GetFrameNavigationFlashClass();

    string ITaskAnnotationWindowContext.CurrentFrameLabel => CurrentFrameLabel;

    string ITaskAnnotationWindowContext.CurrentFileLabel => CurrentFileLabel;

    string ITaskAnnotationWindowContext.ImageLayerStyle => ImageLayerStyle;

    string ITaskAnnotationWindowContext.ImageSizeStatus => ImageSizeStatus;

    string ITaskAnnotationWindowContext.CursorPositionStatus => CursorPositionStatus;

    string ITaskAnnotationWindowContext.SelectionPositionStatus => SelectionPositionStatus;

    string ITaskAnnotationWindowContext.AutoAnnotationProgressLabel => AutoAnnotationProgressLabel;

    string? ITaskAnnotationWindowContext.AutoAnnotationStatusText => autoAnnotationStatusText;

    string ITaskAnnotationWindowContext.ModelOperationProgressText => ModelOperationProgressText;

    string ITaskAnnotationWindowContext.ModelOperationProgressPercentLabel => ModelOperationProgressPercentLabel;

    string? ITaskAnnotationWindowContext.PendingRemoveModelId => pendingRemoveModelId;

    string ITaskAnnotationWindowContext.GetToolClass(EditorTool tool) => GetToolClass(tool);

    string ITaskAnnotationWindowContext.GetFrameNavigationButtonClass(FrameNavigationAction action) => GetFrameNavigationButtonClass(action);

    string ITaskAnnotationWindowContext.GetSuggestionsToolClass() => GetSuggestionsToolClass();

    string ITaskAnnotationWindowContext.GetAnnotationsToolClass() => GetAnnotationsToolClass();

    string ITaskAnnotationWindowContext.GetInspectorTabClass(TaskEditorInspectorTab tab) => GetInspectorTabClass(tab);

    string ITaskAnnotationWindowContext.GetModelOperationProgressClass() => GetModelOperationProgressClass();

    string ITaskAnnotationWindowContext.GetModelOperationProgressBarStyle() => GetModelOperationProgressBarStyle();

    string ITaskAnnotationWindowContext.GetSourceFilterClass(TaskEditorSourceFilter filter) => GetSourceFilterClass(filter);

    string ITaskAnnotationWindowContext.GetShapeSourceClass(EditorShape shape) => GetShapeSourceClass(shape);

    string ITaskAnnotationWindowContext.GetModelStatusClass(AutoAnnotationModelConfig model) => GetModelStatusClass(model);

    string ITaskAnnotationWindowContext.GetMappingRowClass(AutoAnnotationLabelMapping mapping) => GetMappingRowClass(mapping);

    string ITaskAnnotationWindowContext.GetHitLayerClass() => GetHitLayerClass();

    string ITaskAnnotationWindowContext.GetShapeClass(EditorShape shape) => GetShapeClass(shape);

    string ITaskAnnotationWindowContext.GetPasteShapeClass(EditorShape shape) => GetPasteShapeClass(shape);

    string ITaskAnnotationWindowContext.GetSuggestionClass(AutoAnnotationSuggestion suggestion) => GetSuggestionClass(suggestion);

    string ITaskAnnotationWindowContext.GetObjectListItemClass(EditorShape shape) => GetObjectListItemClass(shape);

    string ITaskAnnotationWindowContext.GetLabelPillClass(AnnotationLabelInfo label) => GetLabelPillClass(label);

    string ITaskAnnotationWindowContext.GetShapeStyle(EditorShape shape, AnnotationLabelInfo label) => GetShapeStyle(shape, label);

    string ITaskAnnotationWindowContext.GetSuggestionStyle(AutoAnnotationSuggestion suggestion, AnnotationLabelInfo label) => GetSuggestionStyle(suggestion, label);

    string ITaskAnnotationWindowContext.GetDraftShapeStyle() => GetDraftShapeStyle();

    string ITaskAnnotationWindowContext.GetSelectionMarqueeStyle() => GetSelectionMarqueeStyle();

    string ITaskAnnotationWindowContext.GetFreeformSelectionPolylinePoints() => GetFreeformSelectionPolylinePoints();

    string ITaskAnnotationWindowContext.GetVerticalGuideStyle() => GetVerticalGuideStyle();

    string ITaskAnnotationWindowContext.GetHorizontalGuideStyle() => GetHorizontalGuideStyle();

    string ITaskAnnotationWindowContext.GetGuideCrossStyle() => GetGuideCrossStyle();

    string ITaskAnnotationWindowContext.GetRotateHandleClass(EditorShape shape) => GetRotateHandleClass(shape);

    string ITaskAnnotationWindowContext.GetHandleClass(EditorShape shape, EditorHandle handle) => GetHandleClass(shape, handle);

    string ITaskAnnotationWindowContext.GetSelectionBoundsStyle() => GetSelectionBoundsStyle();

    string ITaskAnnotationWindowContext.GetSelectionRotateHandleClass() => GetSelectionRotateHandleClass();

    string ITaskAnnotationWindowContext.GetSelectionHandleClass(EditorHandle handle) => GetSelectionHandleClass(handle);

    AnnotationLabelInfo ITaskAnnotationWindowContext.GetLabel(int labelId) => GetLabel(labelId);

    bool ITaskAnnotationWindowContext.ShouldShowShapeDetails(EditorShape shape) => ShouldShowShapeDetails(shape);

    bool ITaskAnnotationWindowContext.ShouldShowShapeHandles(EditorShape shape) => ShouldShowShapeHandles(shape);

    bool ITaskAnnotationWindowContext.CanModifySuggestions => CanModifySuggestions;

    bool ITaskAnnotationWindowContext.CanRunModel(AutoAnnotationModelConfig model) => CanRunModel(model);

    bool ITaskAnnotationWindowContext.ShouldShowModelLoadButton(AutoAnnotationModelConfig model) => ShouldShowModelLoadButton(model);

    int ITaskAnnotationWindowContext.GetModelSuggestionCount(AutoAnnotationModelConfig model) => GetModelSuggestionCount(model);

    void ITaskAnnotationWindowContext.SelectTool(EditorTool tool) => SelectTool(tool);

    void ITaskAnnotationWindowContext.StartRectangleTool() => StartRectangleTool();

    void ITaskAnnotationWindowContext.CutSelection() => CutSelection();

    void ITaskAnnotationWindowContext.CopySelection() => CopySelection();

    void ITaskAnnotationWindowContext.DeleteSelection() => DeleteSelection();

    void ITaskAnnotationWindowContext.BeginPaste() => BeginPaste();

    void ITaskAnnotationWindowContext.CopyPreviousFrameAnnotations() => CopyPreviousFrameAnnotations();

    void ITaskAnnotationWindowContext.Undo() => Undo();

    void ITaskAnnotationWindowContext.Redo() => Redo();

    void ITaskAnnotationWindowContext.ResetZoom() => ResetZoom();

    void ITaskAnnotationWindowContext.ToggleShowSuggestions() => ToggleShowSuggestions();

    void ITaskAnnotationWindowContext.ToggleShowAnnotations() => ToggleShowAnnotations();

    void ITaskAnnotationWindowContext.GoFirst() => GoFirst();

    void ITaskAnnotationWindowContext.GoPreviousStep() => GoPreviousStep();

    void ITaskAnnotationWindowContext.GoPrevious() => GoPrevious();

    void ITaskAnnotationWindowContext.GoNext() => GoNext();

    void ITaskAnnotationWindowContext.GoNextStep() => GoNextStep();

    void ITaskAnnotationWindowContext.GoLast() => GoLast();

    void ITaskAnnotationWindowContext.HandleCanvasMouseDown(MouseEventArgs args) => HandleCanvasMouseDown(args);

    void ITaskAnnotationWindowContext.HandleCanvasMouseMove(MouseEventArgs args) => HandleCanvasMouseMove(args);

    void ITaskAnnotationWindowContext.HandleCanvasMouseUp(MouseEventArgs args) => HandleCanvasMouseUp(args);

    void ITaskAnnotationWindowContext.HandleCanvasMouseLeave(MouseEventArgs args) => HandleCanvasMouseLeave(args);

    void ITaskAnnotationWindowContext.HandleCanvasWheel(WheelEventArgs args) => HandleCanvasWheel(args);

    void ITaskAnnotationWindowContext.SuppressBrowserDrag(DragEventArgs args)
    {
    }

    void ITaskAnnotationWindowContext.SelectShapeFromList(string shapeId, MouseEventArgs args) => SelectShapeFromList(shapeId, args);

    void ITaskAnnotationWindowContext.SetHoveredShape(string shapeId) => SetHoveredShape(shapeId);

    void ITaskAnnotationWindowContext.ClearHoveredShape(string shapeId) => ClearHoveredShape(shapeId);

    void ITaskAnnotationWindowContext.SetHoveredSuggestion(string suggestionId) => SetHoveredSuggestion(suggestionId);

    void ITaskAnnotationWindowContext.ClearHoveredSuggestion(string suggestionId) => ClearHoveredSuggestion(suggestionId);

    void ITaskAnnotationWindowContext.SelectLabel(int labelId) => SelectLabel(labelId);

    void ITaskAnnotationWindowContext.AddLatestAutoAnnotationModel() => AddLatestAutoAnnotationModel();

    Task ITaskAnnotationWindowContext.ImportAutoAnnotationModel() => ImportAutoAnnotationModel();

    void ITaskAnnotationWindowContext.DuplicateAutoAnnotationModel(AutoAnnotationModelConfig model) => DuplicateAutoAnnotationModel(model);

    void ITaskAnnotationWindowContext.BeginRemoveAutoAnnotationModel(AutoAnnotationModelConfig model) => BeginRemoveAutoAnnotationModel(model);

    void ITaskAnnotationWindowContext.CancelRemoveAutoAnnotationModel() => CancelRemoveAutoAnnotationModel();

    void ITaskAnnotationWindowContext.RemoveAutoAnnotationModel(AutoAnnotationModelConfig model) => RemoveAutoAnnotationModel(model);

    void ITaskAnnotationWindowContext.MoveAutoAnnotationModel(AutoAnnotationModelConfig model, int delta) => MoveAutoAnnotationModel(model, delta);

    Task ITaskAnnotationWindowContext.ValidateAutoAnnotationModel(AutoAnnotationModelConfig model) => ValidateAutoAnnotationModel(model);

    Task ITaskAnnotationWindowContext.RunFirstAutoAnnotation(bool allFrames) => RunFirstAutoAnnotation(allFrames);

    Task ITaskAnnotationWindowContext.RunAutoAnnotation(AutoAnnotationModelConfig model, bool allFrames) => RunAutoAnnotation(model, allFrames);

    void ITaskAnnotationWindowContext.CancelAutoAnnotationRun() => CancelAutoAnnotationRun();

    void ITaskAnnotationWindowContext.ClearModelSuggestions(AutoAnnotationModelConfig model) => ClearModelSuggestions(model);

    void ITaskAnnotationWindowContext.SetModelEnabled(AutoAnnotationModelConfig model, ChangeEventArgs args) => SetModelEnabled(model, args);

    void ITaskAnnotationWindowContext.SetModelCreateSuggestions(AutoAnnotationModelConfig model, ChangeEventArgs args) => SetModelCreateSuggestions(model, args);

    void ITaskAnnotationWindowContext.UpdateModelConfidence(AutoAnnotationModelConfig model, ChangeEventArgs args) => UpdateModelConfidence(model, args);

    void ITaskAnnotationWindowContext.UpdateModelIoU(AutoAnnotationModelConfig model, ChangeEventArgs args) => UpdateModelIoU(model, args);

    void ITaskAnnotationWindowContext.SetAllModelMappingsEnabled(AutoAnnotationModelConfig model, bool isEnabled) => SetAllModelMappingsEnabled(model, isEnabled);

    void ITaskAnnotationWindowContext.SetMappingEnabled(AutoAnnotationModelConfig model, AutoAnnotationLabelMapping mapping, ChangeEventArgs args) => SetMappingEnabled(model, mapping, args);

    void ITaskAnnotationWindowContext.SetMappingProjectLabel(AutoAnnotationModelConfig model, AutoAnnotationLabelMapping mapping, ChangeEventArgs args) => SetMappingProjectLabel(model, mapping, args);

    void ITaskAnnotationWindowContext.AcceptSuggestion(string suggestionId) => AcceptSuggestion(suggestionId);

    void ITaskAnnotationWindowContext.RemoveSuggestion(string suggestionId) => RemoveSuggestion(suggestionId);

    void ITaskAnnotationWindowContext.AcceptCurrentFrameSuggestions() => AcceptCurrentFrameSuggestions();

    void ITaskAnnotationWindowContext.ClearCurrentFrameSuggestions() => ClearCurrentFrameSuggestions();

    void ITaskAnnotationWindowContext.BeginRemoveCurrentFrame() => BeginRemoveCurrentFrame();

    void ITaskAnnotationWindowContext.CancelRemoveCurrentFrame() => CancelRemoveCurrentFrame();

    Task ITaskAnnotationWindowContext.RemoveCurrentFrame() => RemoveCurrentFrame();

    Task ITaskAnnotationWindowContext.FinishJob() => FinishJob();

    Task ITaskAnnotationWindowContext.CloseWindow() => CloseWindow();

    string ITaskAnnotationWindowContext.FormatShapeSource(EditorShape shape) => FormatShapeSource(shape);

    string ITaskAnnotationWindowContext.FormatSuggestionSource(AutoAnnotationSuggestion suggestion) => FormatSuggestionSource(suggestion);

    string ITaskAnnotationWindowContext.FormatSuggestionConfidence(AutoAnnotationSuggestion suggestion) => FormatSuggestionConfidence(suggestion);

    string ITaskAnnotationWindowContext.FormatModelStatus(AutoAnnotationModelConfig model) => FormatModelStatus(model);

    string ITaskAnnotationWindowContext.FormatResolvedModel(AutoAnnotationModelConfig model) => FormatResolvedModel(model);

    string ITaskAnnotationWindowContext.FormatModelHeaderName(AutoAnnotationModelConfig model) => FormatModelHeaderName(model);

    string ITaskAnnotationWindowContext.FormatModelShortcut(int modelIndex) => FormatModelShortcut(modelIndex);

    string ITaskAnnotationWindowContext.ModelMappingHelpText => ModelMappingHelpText;

    string ITaskAnnotationWindowContext.FormatRect(EditorShape shape) => FormatRect(shape);

    string ITaskAnnotationWindowContext.FormatRect(EditorRect rectangle) => FormatRect(rectangle);

    string ITaskAnnotationWindowContext.FormatShapeLabel(AnnotationLabelInfo label) => FormatShapeLabel(label);

    string ITaskAnnotationWindowContext.FormatStatus(AnnotationTaskStatus status) => FormatStatus(status);

    string ITaskAnnotationWindowContext.GetStatusClass(AnnotationTaskStatus status) => GetStatusClass(status);

    string ITaskAnnotationWindowContext.GetHandleStyle(EditorHandle handle) => GetHandleStyle(handle);

    string ITaskAnnotationWindowContext.GetLabelShortcutText(int labelIndex) => GetLabelShortcutText(labelIndex);

    private ElementReference editorElement;
    private ElementReference editorViewport;
    private ElementReference editorHitLayer;
    private DotNetObjectReference<TaskAnnotationWindow>? dotNetRef;
    private IJSObjectReference? jsModule;
    private IJSObjectReference? resizeObserver;

    private readonly List<EditorShape[]> history = new();
    private readonly ConcurrentDictionary<string, string> imageSourceUrlCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> selectedShapeIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EditorPoint> freeformSelectionPoints = new();
    private readonly List<EditorShape> clipboardShapes = new();
    private readonly List<AutoAnnotationSuggestion> autoAnnotationSuggestions = new();
    private readonly SemaphoreSlim saveLock = new(1, 1);

    private IReadOnlyList<AnnotationFrameInfo> frames = Array.Empty<AnnotationFrameInfo>();
    private List<EditorShape> editorShapes = new();
    private EditorRect? draftRectangle;
    private EditorRect? selectionMarquee;
    private DragState? dragState;
    private EditorPoint? rectangleAnchor;
    private EditorPoint? selectionAnchor;
    private EditorPoint? cursorImagePoint;
    private string? currentImageSourceUrl;
    private int currentFrameIndex;
    private int activeLabelId;
    private int historyIndex = -1;
    private int imageWidth = 1280;
    private int imageHeight = 720;
    private double viewportWidth;
    private double viewportHeight;
    private double viewScale = 1;
    private double viewOffsetX;
    private double viewOffsetY;
    private bool hasUserViewTransform;
    private bool isDirty;
    private bool isAutoSaving;
    private bool isAutoAnnotating;
    private bool isModelLoading;
    private int saveOperationCount;
    private bool showSuggestions = true;
    private bool showAnnotations = true;
    private EditorTool activeTool = EditorTool.Select;
    private TaskEditorInspectorTab activeInspectorTab = TaskEditorInspectorTab.Labels;
    private TaskEditorSourceFilter shapeSourceFilter = TaskEditorSourceFilter.All;
    private string? hoveredShapeId;
    private string? hoveredSuggestionId;
    private string? flashingShapeId;
    private string? autoAnnotationStatusText;
    private double? autoAnnotationProgressPercent;
    private string? activeModelOperationId;
    private string? modelOperationStatusText;
    private double? modelOperationProgressPercent;
    private AutoAnnotationRunResult? lastAutoAnnotationRunResult;
    private EditorPoint? pendingPasteCursor;
    private EditorHandle hoveredHandle = EditorHandle.None;
    private int selectionFlashVersion;
    private int frameNavigationFlashVersion;
    private int frameNavigationButtonFlashVersion;
    private int editorRevision;
    private int pendingRemoveFrameIndex = -1;
    private FrameNavigationDirection frameNavigationDirection = FrameNavigationDirection.None;
    private FrameNavigationAction? frameNavigationButtonFlashAction;
    private string? pendingRemoveModelId;
    private DateTimeOffset? lastSavedAt;
    private CancellationTokenSource? autoSaveCancellationTokenSource;
    private CancellationTokenSource? autoAnnotationCancellationTokenSource;
    private CancellationTokenSource? imagePreloadCancellationTokenSource;
    private SelectionGesture selectionGesture = SelectionGesture.None;
    private bool selectionGestureAdditive;

    [Inject]
    protected INotificationService NotificationService { get; init; } = default!;

    [Inject]
    protected IBlazorWindowAccessor BlazorWindowAccessor { get; init; } = default!;

    [Inject]
    protected IJSRuntime JavaScriptRuntime { get; init; } = default!;

    private YoloEaseProject Project => DataContext.Project;

    private AnnotationTaskInfo TaskInfo => Project.RemoteProject.Tasks.Items.FirstOrDefault(x => x.Id == DataContext.Task.Id) ?? DataContext.Task;

    private int FrameCount => frames.Count;

    private int EffectiveFrameIndex => FrameCount <= 0 ? 0 : Math.Clamp(currentFrameIndex, 0, FrameCount - 1);

    private AnnotationFrameInfo? CurrentFrame => FrameCount <= 0 ? null : frames[EffectiveFrameIndex];

    private string CurrentFrameLabel => FrameCount <= 0 ? "0 / 0" : $"{EffectiveFrameIndex + 1} / {FrameCount}";

    private string CurrentFileLabel => string.IsNullOrWhiteSpace(CurrentFrame?.Name) ? "No file selected" : CurrentFrame.Name;

    private int ZoomPercent => (int)Math.Round(viewScale * 100);

    private bool CanUndo => !IsCanvasInteractionBlocked && historyIndex > 0;

    private bool CanRedo => !IsCanvasInteractionBlocked && historyIndex >= 0 && historyIndex < history.Count - 1;

    private bool CanCopySelection => !IsCanvasInteractionBlocked && EffectiveShapes.Any();

    private bool CanDeleteSelection => !IsCanvasInteractionBlocked && EffectiveShapes.Any();

    private bool CanPasteClipboard => !IsCanvasInteractionBlocked && clipboardShapes.Count > 0;

    private bool CanCopyPreviousFrameAnnotations => !IsCanvasInteractionBlocked && EffectiveFrameIndex > 0 && PreviousFrameShapes.Count > 0;

    private bool CanRemoveCurrentFrame => !IsCanvasInteractionBlocked && Project.RemoteProject.Mode == AnnotationBackendMode.Offline && FrameCount > 0 && CurrentFrame != null;

    private IReadOnlyList<AutoAnnotationModelConfig> AutoAnnotationModels => Project.AutoAnnotation.Models.Items
        .OrderBy(x => x.Order)
        .ToArray();

    private AutoAnnotationModelConfig? FirstRunnableModel => AutoAnnotationModels.FirstOrDefault(x => x.IsEnabled);

    private bool CanRunAutoAnnotation => !IsCanvasInteractionBlocked && HasLabels && FirstRunnableModel != null && FrameCount > 0;

    private bool IsModelOperationActive => isAutoAnnotating || isModelLoading;

    private bool IsSaveOperationActive => Volatile.Read(ref saveOperationCount) > 0;

    private bool IsCanvasInteractionBlocked => IsSaveOperationActive || isAutoAnnotating || isModelLoading;

    private string AutoAnnotationProgressLabel => autoAnnotationProgressPercent == null
        ? "Auto-annotating..."
        : $"Auto-annotating {autoAnnotationProgressPercent.Value:F0}%";

    private string ModelOperationProgressText => modelOperationStatusText ?? "Model operation";

    private string ModelOperationProgressPercentLabel => modelOperationProgressPercent == null
        ? string.Empty
        : $"{modelOperationProgressPercent.Value:F0}%";

    private IReadOnlyList<AnnotationLabelInfo> OrderedLabels => Project.RemoteProject.Labels.Items
        .OrderBy(x => x.Name)
        .ToArray();

    private bool HasLabels => OrderedLabels.Count > 0;

    private bool ShouldShowCreationGuide => activeTool == EditorTool.Rectangle && cursorImagePoint != null && HasLabels;

    private bool IsPasting => activeTool == EditorTool.Paste && pendingPasteCursor != null && clipboardShapes.Count > 0;

    private IReadOnlyList<EditorShape> CurrentFrameShapes => editorShapes
        .Where(x => x.FrameIndex == EffectiveFrameIndex)
        .OrderBy(x => x.Id)
        .ToArray();

    private IReadOnlyList<EditorShape> PreviousFrameShapes => editorShapes
        .Where(x => x.FrameIndex == EffectiveFrameIndex - 1)
        .OrderBy(x => x.Id)
        .ToArray();

    private IReadOnlyList<EditorShape> ListedCurrentFrameShapes => CurrentFrameShapes
        .Where(IsShapeVisibleInShapeList)
        .ToArray();

    private IReadOnlyList<EditorShape> SelectedCurrentFrameShapes => CurrentFrameShapes
        .Where(x => selectedShapeIds.Contains(x.Id))
        .ToArray();

    private IReadOnlyList<EditorShape> EffectiveShapes
    {
        get
        {
            var selected = SelectedCurrentFrameShapes.ToList();
            if (!string.IsNullOrWhiteSpace(hoveredShapeId) &&
                selected.All(x => x.Id != hoveredShapeId) &&
                editorShapes.FirstOrDefault(x => x.Id == hoveredShapeId && x.FrameIndex == EffectiveFrameIndex) is { } hovered)
            {
                selected.Add(hovered);
            }

            return selected;
        }
    }

    private EditorShape? EffectiveShape => EffectiveShapes.FirstOrDefault();

    private SelectionBounds? SelectedSelectionBounds => SelectionBounds.FromShapes(SelectedCurrentFrameShapes);

    private SelectionBounds? EffectiveSelectionBounds => SelectionBounds.FromShapes(EffectiveShapes);

    private bool ShouldShowSelectionBounds => SelectedCurrentFrameShapes.Count > 1 && SelectedSelectionBounds != null;

    private IReadOnlyList<EditorShape> PendingPasteShapes => IsPasting && pendingPasteCursor != null
        ? CreatePasteShapes(pendingPasteCursor, assignNewIds: false).ToArray()
        : Array.Empty<EditorShape>();

    private IReadOnlyList<AutoAnnotationSuggestion> CurrentFrameSuggestions => autoAnnotationSuggestions
        .Where(x => x.FrameIndex == EffectiveFrameIndex)
        .OrderBy(x => x.Id)
        .ToArray();

    private IReadOnlyList<AutoAnnotationSuggestion> VisibleCurrentFrameSuggestions => showSuggestions
        ? CurrentFrameSuggestions
        : Array.Empty<AutoAnnotationSuggestion>();

    private int CurrentFrameSuggestionCount => autoAnnotationSuggestions.Count(x => x.FrameIndex == EffectiveFrameIndex);

    private bool CanModifySuggestions => !IsCanvasInteractionBlocked && CurrentFrameSuggestionCount > 0;

    private string ImageSizeStatus => $"Image {imageWidth}x{imageHeight}";

    private string CursorPositionStatus => cursorImagePoint == null
        ? "Cursor -"
        : $"Cursor x {cursorImagePoint.X:F0}, y {cursorImagePoint.Y:F0}";

    private string SelectionPositionStatus => EffectiveShape is { } shape
        ? EffectiveShapes.Count <= 1
            ? $"Selection x {shape.X:F1}, y {shape.Y:F1}, {FormatRect(shape)}"
            : EffectiveSelectionBounds is { } bounds
                ? $"Selection {EffectiveShapes.Count} objects, x {bounds.X:F1}, y {bounds.Y:F1}, {bounds.Width:F1}x{bounds.Height:F1}px"
                : "Selection -"
        : "Selection -";

    private string ImageLayerStyle => string.Format(
        CultureInfo.InvariantCulture,
        "width:{0}px;height:{1}px;transform:translate({2}px,{3}px) scale({4});",
        imageWidth,
        imageHeight,
        viewOffsetX,
        viewOffsetY,
        viewScale);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (!firstRender)
        {
            return;
        }

        await editorElement.FocusAsync();
        await InitializeResizeObserver();
        await LoadEditorData();
        SubscribeToLatestAutoAnnotationModelUpdates();
        await RefreshLatestAutoAnnotationModels(refreshTrainingDataset: true);
        StartAutoSaveLoop();
        await InvokeAsync(StateHasChanged);
        await FocusEditorCanvas();
    }

    public override async ValueTask DisposeAsync()
    {
        autoSaveCancellationTokenSource?.Cancel();
        autoSaveCancellationTokenSource?.Dispose();
        autoSaveCancellationTokenSource = null;
        autoAnnotationCancellationTokenSource?.Cancel();
        autoAnnotationCancellationTokenSource?.Dispose();
        autoAnnotationCancellationTokenSource = null;
        imagePreloadCancellationTokenSource?.Cancel();
        imagePreloadCancellationTokenSource?.Dispose();
        imagePreloadCancellationTokenSource = null;

        if (resizeObserver != null)
        {
            await resizeObserver.InvokeVoidSafeAsync("dispose");
            await resizeObserver.DisposeJsSafeAsync();
        }

        if (jsModule != null)
        {
            await jsModule.DisposeJsSafeAsync();
        }

        dotNetRef?.DisposeJsSafe();
        saveLock.Dispose();
        await base.DisposeAsync();
    }

    [JSInvokable]
    public async Task OnTaskEditorViewportResized(double width, double height)
    {
        viewportWidth = Math.Max(1, width);
        viewportHeight = Math.Max(1, height);

        if (!hasUserViewTransform)
        {
            FitImageToView();
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task InitializeResizeObserver()
    {
        dotNetRef = DotNetObjectReference.Create(this);
        jsModule = await JavaScriptRuntime.InvokeAsync<IJSObjectReference>("import", JsModulePath);
        resizeObserver = await jsModule.InvokeAsync<IJSObjectReference>("observeSize", editorViewport, dotNetRef);
    }

    private async Task FocusEditorCanvas()
    {
        try
        {
            if (jsModule != null)
            {
                await jsModule.InvokeVoidAsync("focusElement", editorHitLayer);
                return;
            }

            await editorHitLayer.FocusAsync();
        }
        catch (Exception e)
        {
            Log.Warn("Failed to focus task annotation canvas; falling back to editor window focus", e);
            try
            {
                await editorElement.FocusAsync();
            }
            catch (Exception fallbackException)
            {
                Log.Warn("Failed to focus task annotation editor window", fallbackException);
            }
        }
    }

    private async Task LoadEditorData()
    {
        try
        {
            var metadata = await Project.RemoteProject.RetrieveMetadata(TaskInfo.Id);
            frames = metadata.Frames.EmptyIfNull().ToArray();
            editorShapes = (await Project.RemoteProject.RetrieveTaskAnnotations(TaskInfo.Id))
                .Select(EditorShape.FromAnnotation)
                .ToList();
            autoAnnotationSuggestions.Clear();
            hoveredSuggestionId = null;

            EnsureActiveLabel();
            history.Clear();
            historyIndex = -1;
            selectedShapeIds.Clear();
            hoveredShapeId = null;
            isDirty = false;
            PushHistorySnapshot(markDirty: false);
            LoadCurrentFrameImage(resetView: true);
        }
        catch (Exception e)
        {
            await InvokeAsync(() => ShowError(e.Message));
        }
    }

    private void SubscribeToLatestAutoAnnotationModelUpdates()
    {
        Project.TrainingDataset.TrainedModels
            .Connect()
            .Skip(1)
            .Subscribe(_ => InvokeAsync(() => RefreshLatestAutoAnnotationModels(refreshTrainingDataset: false)).AndForget(ignoreExceptions: true))
            .AddTo(Anchors);
    }

    private async Task RefreshLatestAutoAnnotationModels(bool refreshTrainingDataset)
    {
        if (AutoAnnotationModels.All(x => x.SourceKind != AutoAnnotationModelSourceKind.Latest))
        {
            return;
        }

        try
        {
            if (refreshTrainingDataset)
            {
                await Project.TrainingDataset.Refresh();
            }

            var changed = Project.AutoAnnotation.SynchronizeLatestModelReferences(Project.TrainingDataset.TrainedModels.Items.ToArray());
            if (changed)
            {
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception e)
        {
            Log.Warn("Failed to refresh latest auto-annotation model references", e);
        }
    }

    private void StartAutoSaveLoop()
    {
        autoSaveCancellationTokenSource?.Cancel();
        autoSaveCancellationTokenSource?.Dispose();
        autoSaveCancellationTokenSource = new CancellationTokenSource();
        AutoSaveLoop(autoSaveCancellationTokenSource.Token).AndForget();
    }

    private async Task AutoSaveLoop(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(AutoSaveInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(() => AutoSaveIfNeeded(force: false));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            ShowError(e.Message);
        }
    }

    private async Task AutoSaveIfNeeded(bool force)
    {
        if (Project.RemoteProject.Mode != AnnotationBackendMode.Offline)
        {
            return;
        }

        if (!force && (!isDirty || IsCanvasInteractionBlocked || dragState != null || selectionGesture != SelectionGesture.None || activeTool is EditorTool.Rectangle or EditorTool.Paste))
        {
            return;
        }

        if (!isDirty && !force)
        {
            return;
        }

        await PersistEditorAnnotations(GetAutoSaveStatus(), clearDirty: true);
    }

    private AnnotationTaskStatus GetAutoSaveStatus()
    {
        return TaskInfo.Status == AnnotationTaskStatus.Completed ? AnnotationTaskStatus.Completed : AnnotationTaskStatus.InProgress;
    }

    private async Task PersistEditorAnnotations(AnnotationTaskStatus status, bool clearDirty)
    {
        var revisionAtSaveStart = editorRevision;
        BeginSaveOperation();
        await InvokeAsync(StateHasChanged);

        var lockTaken = false;
        await saveLock.WaitAsync();
        lockTaken = true;
        try
        {
            var taskId = TaskInfo.Id;
            var annotations = editorShapes.Select(x => x.ToAnnotation()).ToArray();
            var remoteProject = Project.RemoteProject;

            await remoteProject.SaveTaskAnnotations(taskId, annotations, status);
            lastSavedAt = DateTimeOffset.Now;

            if (clearDirty && editorRevision == revisionAtSaveStart)
            {
                isDirty = false;
            }
        }
        finally
        {
            if (lockTaken)
            {
                saveLock.Release();
            }

            EndSaveOperation();
            await InvokeAsync(StateHasChanged);
        }
    }

    private void BeginSaveOperation()
    {
        if (Interlocked.Increment(ref saveOperationCount) != 1)
        {
            return;
        }

        isAutoSaving = true;
        dragState = null;
        ClearSelectionGesture();
        hoveredHandle = EditorHandle.None;
    }

    private void EndSaveOperation()
    {
        if (Interlocked.Decrement(ref saveOperationCount) > 0)
        {
            return;
        }

        Interlocked.Exchange(ref saveOperationCount, 0);
        isAutoSaving = false;
    }

    private void LoadCurrentFrameImage(bool resetView)
    {
        var frame = CurrentFrame;
        var preservedDragState = !resetView && dragState?.Mode == DragMode.Pan ? dragState : null;
        var preservedCursorImagePoint = !resetView && activeTool == EditorTool.Rectangle ? cursorImagePoint : null;
        currentImageSourceUrl = null;
        selectedShapeIds.Clear();
        hoveredShapeId = null;
        hoveredSuggestionId = null;
        hoveredHandle = EditorHandle.None;
        draftRectangle = null;
        ClearSelectionGesture();
        rectangleAnchor = null;
        cursorImagePoint = null;
        dragState = preservedDragState;
        pendingPasteCursor = null;

        imageWidth = frame?.Width > 0 ? frame.Width : 1280;
        imageHeight = frame?.Height > 0 ? frame.Height : 720;

        var imageFile = ResolveCurrentImageFile();
        if (imageFile?.Exists == true)
        {
            try
            {
                if (frame is not { Width: > 0, Height: > 0 })
                {
                    var imageSize = ImageUtils.GetImageSize(imageFile);
                    imageWidth = imageSize.Width;
                    imageHeight = imageSize.Height;
                }

                currentImageSourceUrl = GetImageSourceUrl(imageFile);
            }
            catch (Exception e)
            {
                ShowError($"Failed to load image {imageFile.Name}: {e.Message}");
            }
        }

        if (preservedCursorImagePoint != null && activeTool == EditorTool.Rectangle)
        {
            cursorImagePoint = ClampToImage(preservedCursorImagePoint);
        }

        if (resetView)
        {
            ResetZoom();
        }

        StartImagePreload(EffectiveFrameIndex);
    }

    private FileInfo? ResolveCurrentImageFile()
    {
        return CurrentFrame == null ? null : ResolveFrameImageFile(CurrentFrame);
    }

    private FileInfo? ResolveFrameImageFile(AnnotationFrameInfo frame)
    {
        var frameName = frame.Name;
        if (string.IsNullOrWhiteSpace(frameName))
        {
            return null;
        }

        var projectFile = Project.RemoteProject.ResolveTaskFrameFile(frameName);
        if (projectFile?.Exists == true)
        {
            return projectFile;
        }

        var fileName = Path.GetFileName(frameName);
        return Project.Assets.Files.Items.FirstOrDefault(x =>
            x.Name.Equals(frameName, StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    private string GetImageSourceUrl(FileInfo imageFile)
    {
        return imageSourceUrlCache.GetOrAdd(
            imageFile.FullName,
            _ => imageFile.ToLocalFileUri().ToString());
    }

    private void StartImagePreload(int centerFrameIndex)
    {
        imagePreloadCancellationTokenSource?.Cancel();
        imagePreloadCancellationTokenSource?.Dispose();
        imagePreloadCancellationTokenSource = new CancellationTokenSource();
        PreloadAdjacentFrameImages(centerFrameIndex, imagePreloadCancellationTokenSource.Token).AndForget();
    }

    private async Task PreloadAdjacentFrameImages(int centerFrameIndex, CancellationToken cancellationToken)
    {
        try
        {
            var imageUrls = new List<string>();
            foreach (var frameIndex in GetImagePreloadIndexes(centerFrameIndex))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frame = frames[frameIndex];
                var imageFile = ResolveFrameImageFile(frame);
                if (imageFile?.Exists != true)
                {
                    continue;
                }

                imageUrls.Add(GetImageSourceUrl(imageFile));
            }

            if (imageUrls.Count <= 0 || jsModule == null)
            {
                return;
            }

            await InvokeAsync(async () =>
            {
                if (jsModule == null)
                {
                    return;
                }

                await jsModule.InvokeVoidAsync("preloadImages", cancellationToken, imageUrls);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.Warn($"Failed to preload task annotation images around frame {centerFrameIndex}", e);
        }
    }

    private IEnumerable<int> GetImagePreloadIndexes(int centerFrameIndex)
    {
        for (var offset = 1; offset <= ImagePreloadRadius; offset++)
        {
            var next = centerFrameIndex + offset;
            if (next >= 0 && next < FrameCount)
            {
                yield return next;
            }

            var previous = centerFrameIndex - offset;
            if (previous >= 0 && previous < FrameCount)
            {
                yield return previous;
            }
        }
    }

    private void ClearImageSourceUrlCache()
    {
        imagePreloadCancellationTokenSource?.Cancel();
        imageSourceUrlCache.Clear();
    }

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (IsSaveOperationActive)
        {
            return;
        }

        if (isAutoAnnotating)
        {
            if (args.Key == "Escape")
            {
                CancelAutoAnnotationRun();
            }

            return;
        }

        if (args.Key == "Escape")
        {
            CancelActiveOperation(clearSelection: true);
            return;
        }

        if (args.CtrlKey || args.MetaKey)
        {
            var key = GetShortcutKey(args);
            if (key == "A")
            {
                SelectAllCurrentFrameShapes();
                return;
            }

            if (key == "X")
            {
                CutSelection();
                return;
            }

            if (key == "C")
            {
                CopySelection();
                return;
            }

            if (key == "V")
            {
                BeginPaste();
                return;
            }

            if (key == "B")
            {
                CopyPreviousFrameAnnotations();
                return;
            }

            if (key == "N")
            {
                CycleEffectiveShapeLabel();
                return;
            }

            if (TrySelectLabelByShortcut(key))
            {
                return;
            }

            if (key == "Z" && !args.ShiftKey)
            {
                Undo();
                return;
            }

            if (key == "Y" || (key == "Z" && args.ShiftKey))
            {
                Redo();
                return;
            }

            return;
        }

        if (IsDeleteShortcut(args))
        {
            if (RemoveHoveredSuggestion())
            {
                return;
            }

            DeleteEffectiveShape();
            return;
        }

        if (args.AltKey)
        {
            if (GetShortcutKey(args) == "N")
            {
                RunFirstAutoAnnotation(args.ShiftKey).AndForget();
                return;
            }

            if (TryRunAutoAnnotationByShortcut(args.Key, args.Code, args.ShiftKey))
            {
                return;
            }

            return;
        }

        switch (GetShortcutKey(args))
        {
            case "M":
                SelectTool(EditorTool.RectangleSelection);
                break;
            case "L":
                SelectTool(EditorTool.FreeformSelection);
                break;
            case "N":
                StartRectangleTool();
                break;
            case "D":
                GoPrevious();
                break;
            case "F":
                GoNext();
                break;
            case "C":
                GoPreviousStep();
                break;
            case "V":
                GoNextStep();
                break;
            case "R":
                ResetZoom();
                break;
        }
    }

    private void HandleCanvasMouseDown(MouseEventArgs args)
    {
        if (IsCanvasInteractionBlocked)
        {
            return;
        }

        FocusEditorCanvas().AndForget(ignoreExceptions: true);

        var screenPoint = new EditorPoint(args.OffsetX, args.OffsetY);
        var imagePoint = ScreenToImage(screenPoint);
        cursorImagePoint = ClampToImage(imagePoint);
        dragState = null;

        if (IsPasting && args.Button == 0)
        {
            CommitPaste(cursorImagePoint);
            return;
        }

        if (activeTool == EditorTool.Rectangle && args.Button == 0)
        {
            HandleRectangleClick(imagePoint);
            return;
        }

        if (activeTool == EditorTool.RectangleSelection && args.Button == 0)
        {
            BeginRectangleSelection(imagePoint, args.CtrlKey || args.ShiftKey || args.MetaKey);
            return;
        }

        if (activeTool == EditorTool.FreeformSelection && args.Button == 0)
        {
            BeginFreeformSelection(imagePoint, args.CtrlKey || args.ShiftKey || args.MetaKey);
            return;
        }

        if (activeTool == EditorTool.Pan || args.Button == 1)
        {
            dragState = DragState.ForPan(screenPoint, viewOffsetX, viewOffsetY);
            hoveredHandle = EditorHandle.None;
            return;
        }

        if (args.Button != 0)
        {
            return;
        }

        var hit = HitTest(imagePoint, includeHandles: true);
        hoveredHandle = hit.Handle;
        hoveredShapeId = hit.Shape?.Id;

        if (args.CtrlKey && hit.Shape != null)
        {
            ToggleShapeSelection(hit.Shape.Id);
            activeTool = EditorTool.Select;
            return;
        }

        if (hit.IsGroup && hit.Handle != EditorHandle.None)
        {
            activeTool = EditorTool.Select;
            dragState = hit.Handle == EditorHandle.Rotate
                ? DragState.ForRotate(screenPoint, imagePoint, SelectedCurrentFrameShapes, SelectedSelectionBounds)
                : DragState.ForResize(screenPoint, imagePoint, SelectedCurrentFrameShapes, hit.Handle, SelectedSelectionBounds);
            return;
        }

        if (hit.Shape != null)
        {
            if (!selectedShapeIds.Contains(hit.Shape.Id))
            {
                SelectOnlyShape(hit.Shape.Id);
                FlashShapeSelection(hit.Shape.Id).AndForget();
            }

            activeTool = EditorTool.Select;
            var dragShapes = SelectedCurrentFrameShapes.Any(x => x.Id == hit.Shape.Id)
                ? SelectedCurrentFrameShapes
                : new[] { hit.Shape };
            dragState = hit.Handle switch
            {
                EditorHandle.Rotate => DragState.ForRotate(screenPoint, imagePoint, dragShapes, SelectionBounds.FromShapes(dragShapes)),
                EditorHandle.None => DragState.ForShapeMove(screenPoint, imagePoint, dragShapes, SelectionBounds.FromShapes(dragShapes)),
                _ => DragState.ForResize(screenPoint, imagePoint, dragShapes, hit.Handle, SelectionBounds.FromShapes(dragShapes)),
            };
            return;
        }

        selectedShapeIds.Clear();
        hoveredShapeId = null;
        hoveredHandle = EditorHandle.None;
        dragState = DragState.ForPan(screenPoint, viewOffsetX, viewOffsetY);
    }

    private void HandleCanvasMouseMove(MouseEventArgs args)
    {
        if (IsCanvasInteractionBlocked)
        {
            return;
        }

        var screenPoint = new EditorPoint(args.OffsetX, args.OffsetY);
        var imagePoint = ScreenToImage(screenPoint);
        cursorImagePoint = ClampToImage(imagePoint);

        if (IsPasting)
        {
            pendingPasteCursor = cursorImagePoint;
        }

        if (activeTool == EditorTool.Rectangle && rectangleAnchor != null)
        {
            draftRectangle = EditorRect.FromPoints(rectangleAnchor, cursorImagePoint).Clamp(imageWidth, imageHeight);
        }

        if (selectionGesture == SelectionGesture.Rectangle && selectionAnchor != null)
        {
            selectionMarquee = EditorRect.FromPoints(selectionAnchor, cursorImagePoint).Clamp(imageWidth, imageHeight);
            return;
        }

        if (selectionGesture == SelectionGesture.Freeform && freeformSelectionPoints.Count > 0)
        {
            AddFreeformSelectionPoint(cursorImagePoint);
            return;
        }

        if (dragState == null)
        {
            UpdateHover(imagePoint);
            return;
        }

        switch (dragState.Mode)
        {
            case DragMode.Pan:
                viewOffsetX = dragState.StartOffsetX + screenPoint.X - dragState.StartScreenPoint.X;
                viewOffsetY = dragState.StartOffsetY + screenPoint.Y - dragState.StartScreenPoint.Y;
                hasUserViewTransform = true;
                dragState.Moved = true;
                break;
            case DragMode.MoveShape:
                MoveDraggedShapes(imagePoint);
                break;
            case DragMode.ResizeShape:
                ResizeDraggedShapes(imagePoint, args.ShiftKey);
                break;
            case DragMode.RotateShape:
                RotateDraggedShapes(imagePoint, args.ShiftKey);
                break;
        }
    }

    private void HandleCanvasMouseUp(MouseEventArgs args)
    {
        if (IsCanvasInteractionBlocked)
        {
            dragState = null;
            ClearSelectionGesture();
            return;
        }

        if (selectionGesture != SelectionGesture.None)
        {
            CompleteSelectionGesture();
            return;
        }

        if (dragState == null)
        {
            return;
        }

        CommitActiveShapeDrag();

        dragState = null;
    }

    private void CommitActiveShapeDrag()
    {
        if (dragState?.Mode is not (DragMode.MoveShape or DragMode.ResizeShape or DragMode.RotateShape))
        {
            return;
        }

        var changedShapes = dragState.StartShapes
            .Select(x => editorShapes.FirstOrDefault(shape => shape.Id == x.Key) is { } shape && !shape.HasSameGeometry(x.Value) ? shape : null)
            .Where(x => x != null)
            .Cast<EditorShape>()
            .ToArray();
        if (changedShapes.Length <= 0)
        {
            return;
        }

        foreach (var shape in changedShapes)
        {
            shape.Source = "manual";
            shape.Confidence = null;
        }

        PushHistorySnapshot(markDirty: true);
    }

    private void HandleCanvasMouseLeave(MouseEventArgs args)
    {
        hoveredShapeId = null;
        hoveredHandle = EditorHandle.None;

        if (selectionGesture != SelectionGesture.None)
        {
            CompleteSelectionGesture();
            return;
        }

        if (dragState?.Mode == DragMode.Pan)
        {
            dragState = null;
        }
    }

    private void HandleCanvasWheel(WheelEventArgs args)
    {
        if (IsCanvasInteractionBlocked)
        {
            return;
        }

        var screenPoint = new EditorPoint(args.OffsetX, args.OffsetY);
        var imagePoint = ScreenToImage(screenPoint);
        var nextScale = args.DeltaY < 0 ? viewScale * WheelZoomFactor : viewScale / WheelZoomFactor;
        nextScale = Clamp(nextScale, MinZoom, MaxZoom);

        viewOffsetX = screenPoint.X - imagePoint.X * nextScale;
        viewOffsetY = screenPoint.Y - imagePoint.Y * nextScale;
        viewScale = nextScale;
        hasUserViewTransform = true;
    }

    private void HandleRectangleClick(EditorPoint imagePoint)
    {
        if (!HasLabels)
        {
            SelectTool(EditorTool.Select);
            return;
        }

        if (!IsInsideImage(imagePoint))
        {
            return;
        }

        var point = ClampToImage(imagePoint);
        cursorImagePoint = point;

        if (rectangleAnchor == null)
        {
            EnsureActiveLabel();
            rectangleAnchor = point;
            draftRectangle = EditorRect.FromPoints(point, point).Clamp(imageWidth, imageHeight);
            selectedShapeIds.Clear();
            return;
        }

        var rectangle = EditorRect.FromPoints(rectangleAnchor, point).Clamp(imageWidth, imageHeight);
        if (rectangle.Width < MinShapeSize || rectangle.Height < MinShapeSize)
        {
            draftRectangle = rectangle;
            return;
        }

        var shape = new EditorShape
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = CvatAnnotationShapeKind.Rectangle,
            FrameIndex = EffectiveFrameIndex,
            LabelId = activeLabelId,
            X = rectangle.X,
            Y = rectangle.Y,
            Width = rectangle.Width,
            Height = rectangle.Height,
            RotationDegrees = 0,
            Source = "manual",
        };

        editorShapes.Add(shape);
        SelectOnlyShape(shape.Id);
        hoveredShapeId = shape.Id;
        rectangleAnchor = null;
        draftRectangle = null;
        activeTool = EditorTool.Select;
        PushHistorySnapshot(markDirty: true);
    }

    private void BeginRectangleSelection(EditorPoint imagePoint, bool additive)
    {
        if (!showAnnotations)
        {
            if (!additive)
            {
                selectedShapeIds.Clear();
            }

            ClearSelectionGesture();
            return;
        }

        if (!IsInsideImage(imagePoint))
        {
            if (!additive)
            {
                selectedShapeIds.Clear();
            }

            ClearSelectionGesture();
            return;
        }

        var point = ClampToImage(imagePoint);
        selectionGesture = SelectionGesture.Rectangle;
        selectionGestureAdditive = additive;
        selectionAnchor = point;
        selectionMarquee = EditorRect.FromPoints(point, point).Clamp(imageWidth, imageHeight);
        freeformSelectionPoints.Clear();
        hoveredShapeId = null;
        hoveredHandle = EditorHandle.None;
        dragState = null;
        if (!additive)
        {
            selectedShapeIds.Clear();
        }
    }

    private void BeginFreeformSelection(EditorPoint imagePoint, bool additive)
    {
        if (!showAnnotations)
        {
            if (!additive)
            {
                selectedShapeIds.Clear();
            }

            ClearSelectionGesture();
            return;
        }

        if (!IsInsideImage(imagePoint))
        {
            if (!additive)
            {
                selectedShapeIds.Clear();
            }

            ClearSelectionGesture();
            return;
        }

        var point = ClampToImage(imagePoint);
        selectionGesture = SelectionGesture.Freeform;
        selectionGestureAdditive = additive;
        selectionAnchor = null;
        selectionMarquee = null;
        freeformSelectionPoints.Clear();
        freeformSelectionPoints.Add(point);
        hoveredShapeId = null;
        hoveredHandle = EditorHandle.None;
        dragState = null;
        if (!additive)
        {
            selectedShapeIds.Clear();
        }
    }

    private void AddFreeformSelectionPoint(EditorPoint point)
    {
        if (freeformSelectionPoints.Count <= 0)
        {
            freeformSelectionPoints.Add(point);
            return;
        }

        var previous = freeformSelectionPoints[^1];
        if (Distance(previous, point) < ScreenDistanceToImageUnits(FreeformSelectionPointSpacingPixels))
        {
            return;
        }

        freeformSelectionPoints.Add(point);
    }

    private void CompleteSelectionGesture()
    {
        switch (selectionGesture)
        {
            case SelectionGesture.Rectangle:
                ApplyRectangleSelection(selectionMarquee, selectionGestureAdditive);
                break;
            case SelectionGesture.Freeform:
                ApplyFreeformSelection(freeformSelectionPoints, selectionGestureAdditive);
                break;
        }

        ClearSelectionGesture();
    }

    private void ClearSelectionGesture()
    {
        selectionGesture = SelectionGesture.None;
        selectionGestureAdditive = false;
        selectionAnchor = null;
        selectionMarquee = null;
        freeformSelectionPoints.Clear();
    }

    private void ApplyRectangleSelection(EditorRect? selectionRectangle, bool additive)
    {
        if (!showAnnotations)
        {
            return;
        }

        if (selectionRectangle == null)
        {
            return;
        }

        var minimumSize = ScreenDistanceToImageUnits(2);
        if (selectionRectangle.Width < minimumSize && selectionRectangle.Height < minimumSize)
        {
            return;
        }

        var shapes = CurrentFrameShapes
            .Where(shape => Intersects(selectionRectangle, new EditorRect(shape.X, shape.Y, shape.Width, shape.Height)))
            .ToArray();
        ApplyShapeSelection(shapes, additive);
    }

    private void ApplyFreeformSelection(IReadOnlyList<EditorPoint> polygon, bool additive)
    {
        if (!showAnnotations)
        {
            return;
        }

        if (polygon.Count < 3)
        {
            return;
        }

        var bounds = EditorRect.FromPoints(
            new EditorPoint(polygon.Min(x => x.X), polygon.Min(x => x.Y)),
            new EditorPoint(polygon.Max(x => x.X), polygon.Max(x => x.Y)));
        var minimumSize = ScreenDistanceToImageUnits(2);
        if (bounds.Width < minimumSize && bounds.Height < minimumSize)
        {
            return;
        }

        var shapes = CurrentFrameShapes
            .Where(shape => IsPointInsidePolygon(shape.Center, polygon))
            .ToArray();
        ApplyShapeSelection(shapes, additive);
    }

    private void ApplyShapeSelection(IReadOnlyList<EditorShape> shapes, bool additive)
    {
        if (!additive)
        {
            selectedShapeIds.Clear();
        }

        foreach (var shape in shapes)
        {
            selectedShapeIds.Add(shape.Id);
        }

        hoveredShapeId = shapes.FirstOrDefault()?.Id;
        hoveredHandle = EditorHandle.None;
        if (hoveredShapeId != null)
        {
            FlashShapeSelection(hoveredShapeId).AndForget();
        }
    }

    private void MoveDraggedShapes(EditorPoint imagePoint)
    {
        if (dragState == null || dragState.StartShapes.Count <= 0)
        {
            return;
        }

        var deltaX = imagePoint.X - dragState.StartImagePoint.X;
        var deltaY = imagePoint.Y - dragState.StartImagePoint.Y;

        if (dragState.StartBounds is { } bounds)
        {
            deltaX = Clamp(deltaX, -bounds.X, imageWidth - bounds.Right);
            deltaY = Clamp(deltaY, -bounds.Y, imageHeight - bounds.Bottom);
        }

        foreach (var startShape in dragState.StartShapes.Values)
        {
            var shape = editorShapes.FirstOrDefault(x => x.Id == startShape.Id);
            if (shape == null)
            {
                continue;
            }

            shape.X = Clamp(startShape.X + deltaX, 0, Math.Max(0, imageWidth - shape.Width));
            shape.Y = Clamp(startShape.Y + deltaY, 0, Math.Max(0, imageHeight - shape.Height));
        }
    }

    private void ResizeDraggedShapes(EditorPoint imagePoint, bool preserveAspectRatio)
    {
        if (dragState == null || dragState.StartShapes.Count <= 0)
        {
            return;
        }

        if (dragState.StartShapes.Count == 1)
        {
            var startShape = dragState.StartShapes.Values.First();
            var shape = editorShapes.FirstOrDefault(x => x.Id == startShape.Id);
            if (shape == null)
            {
                return;
            }

            var resized = startShape.ResizeFromHandle(dragState.Handle, imagePoint, MinShapeSize, preserveAspectRatio);
            resized.ClampUnrotatedBounds(imageWidth, imageHeight);
            shape.CopyGeometryFrom(resized);
            return;
        }

        if (dragState.StartBounds == null)
        {
            return;
        }

        var nextBounds = dragState.StartBounds
            .ResizeFromHandle(dragState.Handle, imagePoint, MinShapeSize, preserveAspectRatio)
            .Clamp(imageWidth, imageHeight, MinShapeSize);
        var scaleX = nextBounds.Width / Math.Max(MinShapeSize, dragState.StartBounds.Width);
        var scaleY = nextBounds.Height / Math.Max(MinShapeSize, dragState.StartBounds.Height);

        foreach (var startShape in dragState.StartShapes.Values)
        {
            var shape = editorShapes.FirstOrDefault(x => x.Id == startShape.Id);
            if (shape == null)
            {
                continue;
            }

            var startCenter = startShape.Center;
            var relativeX = (startCenter.X - dragState.StartBounds.X) / Math.Max(MinShapeSize, dragState.StartBounds.Width);
            var relativeY = (startCenter.Y - dragState.StartBounds.Y) / Math.Max(MinShapeSize, dragState.StartBounds.Height);
            var nextWidth = Math.Max(MinShapeSize, startShape.Width * scaleX);
            var nextHeight = Math.Max(MinShapeSize, startShape.Height * scaleY);
            var nextCenter = new EditorPoint(
                nextBounds.X + relativeX * nextBounds.Width,
                nextBounds.Y + relativeY * nextBounds.Height);

            shape.X = Clamp(nextCenter.X - nextWidth / 2.0, 0, Math.Max(0, imageWidth - nextWidth));
            shape.Y = Clamp(nextCenter.Y - nextHeight / 2.0, 0, Math.Max(0, imageHeight - nextHeight));
            shape.Width = Clamp(nextWidth, MinShapeSize, imageWidth);
            shape.Height = Clamp(nextHeight, MinShapeSize, imageHeight);
            shape.RotationDegrees = startShape.RotationDegrees;
        }
    }

    private void RotateDraggedShapes(EditorPoint imagePoint, bool snapToGrid)
    {
        if (dragState == null || dragState.StartShapes.Count <= 0)
        {
            return;
        }

        if (dragState.StartShapes.Count == 1)
        {
            var startShape = dragState.StartShapes.Values.First();
            var shape = editorShapes.FirstOrDefault(x => x.Id == startShape.Id);
            if (shape == null)
            {
                return;
            }

            var center = startShape.Center;
            var rotation = NormalizeRotation(RadiansToDegrees(Math.Atan2(imagePoint.Y - center.Y, imagePoint.X - center.X)) + 90);
            shape.RotationDegrees = snapToGrid
                ? NormalizeRotation(Math.Round(rotation / RotationSnapDegrees) * RotationSnapDegrees)
                : rotation;
            return;
        }

        if (dragState.StartBounds == null)
        {
            return;
        }

        var centerPoint = dragState.StartBounds.Center;
        var startAngle = RadiansToDegrees(Math.Atan2(dragState.StartImagePoint.Y - centerPoint.Y, dragState.StartImagePoint.X - centerPoint.X));
        var nextAngle = RadiansToDegrees(Math.Atan2(imagePoint.Y - centerPoint.Y, imagePoint.X - centerPoint.X));
        var delta = NormalizeSignedRotation(nextAngle - startAngle);
        if (snapToGrid)
        {
            delta = Math.Round(delta / RotationSnapDegrees) * RotationSnapDegrees;
        }

        foreach (var startShape in dragState.StartShapes.Values)
        {
            var shape = editorShapes.FirstOrDefault(x => x.Id == startShape.Id);
            if (shape == null)
            {
                continue;
            }

            var nextCenter = RotatePoint(startShape.Center, centerPoint, delta);
            shape.X = Clamp(nextCenter.X - startShape.Width / 2.0, 0, Math.Max(0, imageWidth - startShape.Width));
            shape.Y = Clamp(nextCenter.Y - startShape.Height / 2.0, 0, Math.Max(0, imageHeight - startShape.Height));
            shape.Width = startShape.Width;
            shape.Height = startShape.Height;
            shape.RotationDegrees = NormalizeRotation(startShape.RotationDegrees + delta);
        }
    }

    private void GoFirst()
    {
        TriggerFrameNavigationButtonFlash(FrameNavigationAction.First);
        SetFrameIndex(0).AndForget();
    }

    private void GoPreviousStep()
    {
        TriggerFrameNavigationButtonFlash(FrameNavigationAction.PreviousStep);
        MoveFrame(-FrameStep).AndForget();
    }

    private void GoPrevious()
    {
        TriggerFrameNavigationButtonFlash(FrameNavigationAction.Previous);
        MoveFrame(-1).AndForget();
    }

    private void GoNext()
    {
        TriggerFrameNavigationButtonFlash(FrameNavigationAction.Next);
        MoveFrame(1).AndForget();
    }

    private void GoNextStep()
    {
        TriggerFrameNavigationButtonFlash(FrameNavigationAction.NextStep);
        MoveFrame(FrameStep).AndForget();
    }

    private void GoLast()
    {
        TriggerFrameNavigationButtonFlash(FrameNavigationAction.Last);
        SetFrameIndex(FrameCount - 1).AndForget();
    }

    private async Task MoveFrame(int delta)
    {
        await SetFrameIndex(EffectiveFrameIndex + delta);
    }

    private async Task SetFrameIndex(int frameIndex)
    {
        var previousFrameIndex = currentFrameIndex;
        var targetFrameIndex = FrameCount <= 0 ? 0 : Math.Clamp(frameIndex, 0, FrameCount - 1);
        if (targetFrameIndex == currentFrameIndex && !string.IsNullOrWhiteSpace(currentImageSourceUrl))
        {
            return;
        }

        CommitActiveShapeDrag();
        currentFrameIndex = targetFrameIndex;
        TriggerFrameNavigationFlash(targetFrameIndex > previousFrameIndex
            ? FrameNavigationDirection.Next
            : targetFrameIndex < previousFrameIndex
                ? FrameNavigationDirection.Previous
                : FrameNavigationDirection.None);
        LoadCurrentFrameImage(resetView: false);
        await InvokeAsync(StateHasChanged);
    }

    private void TriggerFrameNavigationFlash(FrameNavigationDirection direction)
    {
        if (direction == FrameNavigationDirection.None)
        {
            return;
        }

        frameNavigationDirection = direction;
        frameNavigationFlashVersion++;
    }

    private void TriggerFrameNavigationButtonFlash(FrameNavigationAction action)
    {
        frameNavigationButtonFlashAction = action;
        frameNavigationButtonFlashVersion++;
    }

    private string GetFrameNavigationFlashClass()
    {
        var directionClass = frameNavigationDirection switch
        {
            FrameNavigationDirection.Previous => "is-left",
            FrameNavigationDirection.Next => "is-right",
            _ => string.Empty,
        };

        return string.IsNullOrWhiteSpace(directionClass)
            ? "task-editor-frame-navigation-flash"
            : $"task-editor-frame-navigation-flash {directionClass}";
    }

    private string GetFrameNavigationButtonClass(FrameNavigationAction action)
    {
        const string classes = "task-editor-icon-button task-editor-frame-nav-button";
        if (frameNavigationButtonFlashAction != action)
        {
            return classes;
        }

        var flashClass = frameNavigationButtonFlashVersion % 2 == 0
            ? "is-flash-even"
            : "is-flash-odd";
        return $"{classes} {flashClass}";
    }

    private void SelectTool(EditorTool tool)
    {
        if (IsCanvasInteractionBlocked)
        {
            return;
        }

        if (tool == EditorTool.Rectangle && !HasLabels)
        {
            tool = EditorTool.Select;
        }

        activeTool = tool;
        draftRectangle = null;
        rectangleAnchor = null;
        ClearSelectionGesture();
        dragState = null;
        pendingPasteCursor = null;
    }

    private void StartRectangleTool()
    {
        if (IsCanvasInteractionBlocked)
        {
            return;
        }

        if (!HasLabels)
        {
            return;
        }

        EnsureActiveLabel();
        SelectTool(EditorTool.Rectangle);
        selectedShapeIds.Clear();
    }

    private void SelectLabel(int labelId)
    {
        if (!Project.RemoteProject.Labels.Items.Any(x => x.Id == labelId))
        {
            return;
        }

        activeLabelId = labelId;

        var shapes = EffectiveShapes.Where(x => x.LabelId != labelId).ToArray();
        if (shapes.Length <= 0)
        {
            return;
        }

        foreach (var shape in shapes)
        {
            shape.LabelId = labelId;
            shape.Source = "manual";
            shape.Confidence = null;
        }

        PushHistorySnapshot(markDirty: true);
    }

    private void SelectShapeFromList(string shapeId, MouseEventArgs args)
    {
        if (args.CtrlKey || args.MetaKey)
        {
            ToggleShapeSelection(shapeId);
            return;
        }

        SelectShape(shapeId);
    }

    private void SelectShape(string shapeId)
    {
        SelectOnlyShape(shapeId);
        hoveredShapeId = shapeId;
        activeTool = EditorTool.Select;
        FlashShapeSelection(shapeId).AndForget();

        var shape = editorShapes.FirstOrDefault(x => x.Id == shapeId);
        if (shape != null)
        {
            activeLabelId = shape.LabelId;
        }
    }

    private void SelectOnlyShape(string shapeId)
    {
        selectedShapeIds.Clear();
        if (!string.IsNullOrWhiteSpace(shapeId))
        {
            selectedShapeIds.Add(shapeId);
        }
    }

    private void ToggleShapeSelection(string shapeId)
    {
        if (selectedShapeIds.Contains(shapeId))
        {
            selectedShapeIds.Remove(shapeId);
        }
        else
        {
            selectedShapeIds.Add(shapeId);
            FlashShapeSelection(shapeId).AndForget();
        }

        hoveredShapeId = shapeId;
    }

    private void SelectAllCurrentFrameShapes()
    {
        selectedShapeIds.Clear();
        foreach (var shape in CurrentFrameShapes)
        {
            selectedShapeIds.Add(shape.Id);
        }

        activeTool = EditorTool.Select;
        pendingPasteCursor = null;
    }

    private void SetHoveredShape(string shapeId)
    {
        hoveredShapeId = shapeId;
        hoveredSuggestionId = null;
    }

    private void ClearHoveredShape(string shapeId)
    {
        if (hoveredShapeId == shapeId)
        {
            hoveredShapeId = null;
        }
    }

    private void SetHoveredSuggestion(string suggestionId)
    {
        hoveredSuggestionId = suggestionId;
        hoveredShapeId = null;
        hoveredHandle = EditorHandle.None;
    }

    private void ClearHoveredSuggestion(string suggestionId)
    {
        if (hoveredSuggestionId == suggestionId)
        {
            hoveredSuggestionId = null;
        }
    }

    private void CancelActiveOperation(bool clearSelection)
    {
        if (dragState != null)
        {
            if (dragState.Mode == DragMode.Pan)
            {
                viewOffsetX = dragState.StartOffsetX;
                viewOffsetY = dragState.StartOffsetY;
            }
            else if (dragState.StartShapes.Count > 0)
            {
                foreach (var startShape in dragState.StartShapes.Values)
                {
                    var shape = editorShapes.FirstOrDefault(x => x.Id == startShape.Id);
                    shape?.CopyGeometryFrom(startShape);
                }
            }
        }

        draftRectangle = null;
        rectangleAnchor = null;
        ClearSelectionGesture();
        dragState = null;
        activeTool = EditorTool.Select;
        pendingPasteCursor = null;
        hoveredHandle = EditorHandle.None;
        hoveredSuggestionId = null;

        if (clearSelection)
        {
            selectedShapeIds.Clear();
            hoveredShapeId = null;
        }
    }

    private void DeleteEffectiveShape()
    {
        if (IsCanvasInteractionBlocked)
        {
            return;
        }

        var shapeIds = EffectiveShapes.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (shapeIds.Count <= 0)
        {
            return;
        }

        var removed = editorShapes.RemoveAll(x => shapeIds.Contains(x.Id));
        if (removed <= 0)
        {
            return;
        }

        selectedShapeIds.RemoveWhere(shapeIds.Contains);
        if (!string.IsNullOrWhiteSpace(hoveredShapeId) && shapeIds.Contains(hoveredShapeId))
        {
            hoveredShapeId = null;
        }

        PushHistorySnapshot(markDirty: true);
    }

    private void DeleteSelection()
    {
        if (!CanDeleteSelection)
        {
            return;
        }

        DeleteEffectiveShape();
    }

    private void CopySelection()
    {
        var shapes = EffectiveShapes.ToArray();
        if (shapes.Length <= 0)
        {
            return;
        }

        clipboardShapes.Clear();
        clipboardShapes.AddRange(shapes.Select(x => x.Clone()));
    }

    private void CutSelection()
    {
        if (!CanCopySelection)
        {
            return;
        }

        CopySelection();
        DeleteEffectiveShape();
    }

    private void BeginPaste()
    {
        if (IsCanvasInteractionBlocked)
        {
            return;
        }

        if (!CanPasteClipboard)
        {
            return;
        }

        CancelActiveOperation(clearSelection: false);
        activeTool = EditorTool.Paste;
        pendingPasteCursor = cursorImagePoint ?? new EditorPoint(imageWidth / 2.0, imageHeight / 2.0);
        selectedShapeIds.Clear();
        hoveredShapeId = null;
        hoveredHandle = EditorHandle.None;
    }

    private void CopyPreviousFrameAnnotations()
    {
        if (!CanCopyPreviousFrameAnnotations)
        {
            return;
        }

        CancelActiveOperation(clearSelection: true);
        var copiedShapes = TaskAnnotationShapeOperations.CopyToFrameAsManual(
            PreviousFrameShapes,
            EffectiveFrameIndex,
            imageWidth,
            imageHeight);
        if (copiedShapes.Count <= 0)
        {
            return;
        }

        editorShapes.AddRange(copiedShapes);
        selectedShapeIds.Clear();
        foreach (var shape in copiedShapes)
        {
            selectedShapeIds.Add(shape.Id);
        }

        hoveredShapeId = copiedShapes.FirstOrDefault()?.Id;
        hoveredHandle = EditorHandle.None;
        activeTool = EditorTool.Select;
        pendingPasteCursor = null;
        PushHistorySnapshot(markDirty: true);
    }

    private void CommitPaste(EditorPoint imagePoint)
    {
        if (!CanPasteClipboard)
        {
            return;
        }

        var pastedShapes = CreatePasteShapes(imagePoint, assignNewIds: true).ToArray();
        if (pastedShapes.Length <= 0)
        {
            return;
        }

        editorShapes.AddRange(pastedShapes);
        selectedShapeIds.Clear();
        foreach (var shape in pastedShapes)
        {
            selectedShapeIds.Add(shape.Id);
        }

        hoveredShapeId = pastedShapes.FirstOrDefault()?.Id;
        pendingPasteCursor = null;
        activeTool = EditorTool.Select;
        PushHistorySnapshot(markDirty: true);
    }

    private IEnumerable<EditorShape> CreatePasteShapes(EditorPoint cursorPoint, bool assignNewIds)
    {
        if (clipboardShapes.Count <= 0)
        {
            yield break;
        }

        var bounds = SelectionBounds.FromShapes(clipboardShapes);
        if (bounds == null)
        {
            yield break;
        }

        var targetX = Clamp(cursorPoint.X - bounds.Width / 2.0, 0, Math.Max(0, imageWidth - bounds.Width));
        var targetY = Clamp(cursorPoint.Y - bounds.Height / 2.0, 0, Math.Max(0, imageHeight - bounds.Height));
        var deltaX = targetX - bounds.X;
        var deltaY = targetY - bounds.Y;
        var index = 0;

        foreach (var shape in clipboardShapes)
        {
            var clone = shape.CloneWithId(assignNewIds ? Guid.NewGuid().ToString("N") : $"paste-preview-{index++}");
            clone.FrameIndex = EffectiveFrameIndex;
            clone.X = Clamp(shape.X + deltaX, 0, Math.Max(0, imageWidth - shape.Width));
            clone.Y = Clamp(shape.Y + deltaY, 0, Math.Max(0, imageHeight - shape.Height));
            clone.Source = "manual";
            clone.Confidence = null;
            yield return clone;
        }
    }

    private async Task FlashShapeSelection(string shapeId)
    {
        var version = ++selectionFlashVersion;
        flashingShapeId = shapeId;
        await InvokeAsync(StateHasChanged);
        await Task.Delay(260);

        if (selectionFlashVersion != version || flashingShapeId != shapeId)
        {
            return;
        }

        flashingShapeId = null;
        await InvokeAsync(StateHasChanged);
    }

    private void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        historyIndex--;
        RestoreHistorySnapshot(history[historyIndex]);
        MarkDirty();
    }

    private void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        historyIndex++;
        RestoreHistorySnapshot(history[historyIndex]);
        MarkDirty();
    }

    private void ResetZoom()
    {
        hasUserViewTransform = false;
        FitImageToView();
    }

    private void ToggleShowSuggestions()
    {
        showSuggestions = !showSuggestions;
        if (!showSuggestions)
        {
            hoveredSuggestionId = null;
        }
    }

    private void ToggleShowAnnotations()
    {
        showAnnotations = !showAnnotations;
        if (showAnnotations)
        {
            return;
        }

        selectedShapeIds.Clear();
        hoveredShapeId = null;
        hoveredHandle = EditorHandle.None;
        dragState = null;
        ClearSelectionGesture();
    }

    private void FitImageToView()
    {
        var width = Math.Max(1, viewportWidth);
        var height = Math.Max(1, viewportHeight);
        var usableWidth = Math.Max(1, width - ViewPadding * 2);
        var usableHeight = Math.Max(1, height - ViewPadding * 2);
        var scale = Math.Min(usableWidth / Math.Max(1, imageWidth), usableHeight / Math.Max(1, imageHeight));

        viewScale = Clamp(scale, MinZoom, MaxZoom);
        viewOffsetX = (width - imageWidth * viewScale) / 2;
        viewOffsetY = (height - imageHeight * viewScale) / 2;
    }

    private void UpdateHover(EditorPoint imagePoint)
    {
        if (activeTool is EditorTool.Rectangle or EditorTool.RectangleSelection or EditorTool.FreeformSelection)
        {
            hoveredShapeId = null;
            hoveredHandle = EditorHandle.None;
            return;
        }

        var hit = HitTest(imagePoint, includeHandles: true);
        hoveredShapeId = hit.Shape?.Id;
        hoveredHandle = hit.Handle;
    }

    private EditorHit HitTest(EditorPoint imagePoint, bool includeHandles)
    {
        if (!showAnnotations)
        {
            return EditorHit.None;
        }

        if (!IsInsideImage(imagePoint))
        {
            return EditorHit.None;
        }

        var tolerance = ScreenDistanceToImageUnits(HitTolerancePixels);
        var rotateHandleOffset = ScreenDistanceToImageUnits(RotateHandleOffset);

        if (includeHandles)
        {
            if (SelectedCurrentFrameShapes.Count > 1 && SelectedSelectionBounds is { } groupBounds)
            {
                var groupHandle = groupBounds.HitHandle(imagePoint, tolerance, rotateHandleOffset);
                if (groupHandle != EditorHandle.None)
                {
                    return new EditorHit(null, groupHandle, IsGroup: true);
                }
            }

            var handleShapeIds = selectedShapeIds
                .Concat(string.IsNullOrWhiteSpace(hoveredShapeId) ? Array.Empty<string>() : new[] { hoveredShapeId })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var shapeId in handleShapeIds)
            {
                var handleShape = CurrentFrameShapes.FirstOrDefault(x => x.Id == shapeId);
                var handle = handleShape?.HitHandle(imagePoint, tolerance, rotateHandleOffset) ?? EditorHandle.None;
                if (handleShape != null && handle != EditorHandle.None)
                {
                    return new EditorHit(handleShape, handle);
                }

                if (handleShape != null && handleShape.Contains(imagePoint, tolerance + rotateHandleOffset))
                {
                    return new EditorHit(handleShape, EditorHandle.None);
                }
            }
        }

        foreach (var shape in CurrentFrameShapes.Reverse())
        {
            if (shape.Contains(imagePoint, tolerance))
            {
                return new EditorHit(shape, EditorHandle.None);
            }
        }

        return EditorHit.None;
    }

    private EditorPoint ScreenToImage(EditorPoint screenPoint)
    {
        return new EditorPoint(
            (screenPoint.X - viewOffsetX) / viewScale,
            (screenPoint.Y - viewOffsetY) / viewScale);
    }

    private EditorPoint ImageToScreen(EditorPoint imagePoint)
    {
        return new EditorPoint(
            viewOffsetX + imagePoint.X * viewScale,
            viewOffsetY + imagePoint.Y * viewScale);
    }

    private EditorRect ImageToScreen(EditorRect imageRect)
    {
        var topLeft = ImageToScreen(new EditorPoint(imageRect.X, imageRect.Y));
        return new EditorRect(
            SnapScreenPixel(topLeft.X),
            SnapScreenPixel(topLeft.Y),
            Math.Max(0, SnapScreenPixel(imageRect.Width * viewScale)),
            Math.Max(0, SnapScreenPixel(imageRect.Height * viewScale)));
    }

    private double ScreenDistanceToImageUnits(double screenPixels)
    {
        return Math.Max(3, screenPixels / Math.Max(MinZoom, viewScale));
    }

    private static double SnapScreenPixel(double value)
    {
        return Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private EditorPoint ClampToImage(EditorPoint imagePoint)
    {
        return new EditorPoint(
            Clamp(imagePoint.X, 0, imageWidth),
            Clamp(imagePoint.Y, 0, imageHeight));
    }

    private bool IsInsideImage(EditorPoint imagePoint)
    {
        return imagePoint.X >= 0 &&
               imagePoint.Y >= 0 &&
               imagePoint.X <= imageWidth &&
               imagePoint.Y <= imageHeight;
    }

    private bool TrySelectLabelByShortcut(string? key)
    {
        if (key is not { Length: 1 } || !char.IsDigit(key[0]))
        {
            return false;
        }

        var index = key[0] - '1';
        if (index < 0)
        {
            return false;
        }

        var labels = OrderedLabels;
        if (index >= labels.Count)
        {
            return true;
        }

        SelectLabel(labels[index].Id);
        return true;
    }

    private void CycleEffectiveShapeLabel()
    {
        var labels = OrderedLabels;
        if (labels.Count <= 0)
        {
            return;
        }

        var currentLabelId = EffectiveShape?.LabelId ?? activeLabelId;
        var currentIndex = labels.ToList().FindIndex(x => x.Id == currentLabelId);
        var nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % labels.Count;
        SelectLabel(labels[nextIndex].Id);
    }

    private void PushHistorySnapshot(bool markDirty)
    {
        var snapshot = editorShapes.Select(x => x.Clone()).ToArray();
        if (historyIndex >= 0 && SnapshotsEqual(history[historyIndex], snapshot))
        {
            return;
        }

        if (historyIndex < history.Count - 1)
        {
            history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
        }

        history.Add(snapshot);
        historyIndex = history.Count - 1;
        if (markDirty)
        {
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        isDirty = true;
        editorRevision++;
    }

    private void RestoreHistorySnapshot(EditorShape[] snapshot)
    {
        editorShapes = snapshot.Select(x => x.Clone()).ToList();
        selectedShapeIds.RemoveWhere(x => editorShapes.All(shape => shape.Id != x));
        hoveredShapeId = null;
        hoveredHandle = EditorHandle.None;
        draftRectangle = null;
        rectangleAnchor = null;
        ClearSelectionGesture();
        dragState = null;
        pendingPasteCursor = null;
    }

    private static bool SnapshotsEqual(IReadOnlyList<EditorShape> left, IReadOnlyList<EditorShape> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.Zip(right).All(x => x.First.HasSameGeometry(x.Second));
    }

    private async Task FinishJob()
    {
        try
        {
            if (Project.RemoteProject.Mode != AnnotationBackendMode.Offline)
            {
                await Project.RemoteProject.UpdateTaskStatus(TaskInfo.Id, AnnotationTaskStatus.Completed);
                await Project.Refresh();
                isDirty = false;
                CloseWindowCore();
                return;
            }

            await PersistEditorAnnotations(AnnotationTaskStatus.Completed, clearDirty: true);
            await Project.Refresh();
            isDirty = false;
            CloseWindowCore();
        }
        catch (Exception e)
        {
            ShowError(e.Message);
        }
    }

    private async Task CloseWindow()
    {
        try
        {
            await AutoSaveIfNeeded(force: true);
        }
        catch (Exception e)
        {
            ShowError(e.Message);
            return;
        }

        CloseWindowCore();
    }

    private void CloseWindowCore()
    {
        BlazorWindowAccessor.Window.Close();
    }

    private void AcceptSuggestion(string suggestionId)
    {
        if (IsCanvasInteractionBlocked)
        {
            return;
        }

        var suggestion = autoAnnotationSuggestions.FirstOrDefault(x => string.Equals(x.Id, suggestionId, StringComparison.OrdinalIgnoreCase));
        if (suggestion == null)
        {
            return;
        }

        var shape = suggestion.ToManualShape();
        editorShapes.Add(shape);
        autoAnnotationSuggestions.Remove(suggestion);
        hoveredSuggestionId = null;
        selectedShapeIds.Clear();
        selectedShapeIds.Add(shape.Id);
        hoveredShapeId = shape.Id;
        PushHistorySnapshot(markDirty: true);
    }

    private void RemoveSuggestion(string suggestionId)
    {
        if (IsCanvasInteractionBlocked)
        {
            return;
        }

        RemoveSuggestionCore(suggestionId);
    }

    private bool RemoveHoveredSuggestion()
    {
        if (IsCanvasInteractionBlocked || string.IsNullOrWhiteSpace(hoveredSuggestionId))
        {
            return false;
        }

        return RemoveSuggestionCore(hoveredSuggestionId);
    }

    private bool RemoveSuggestionCore(string suggestionId)
    {
        if (AutoAnnotationSuggestionOperations.RemoveSuggestion(autoAnnotationSuggestions, suggestionId) <= 0)
        {
            return false;
        }

        if (string.Equals(hoveredSuggestionId, suggestionId, StringComparison.OrdinalIgnoreCase))
        {
            hoveredSuggestionId = null;
        }

        return true;
    }

    private void AcceptCurrentFrameSuggestions()
    {
        if (IsCanvasInteractionBlocked)
        {
            return;
        }

        var suggestions = CurrentFrameSuggestions.ToArray();
        if (suggestions.Length <= 0)
        {
            return;
        }

        selectedShapeIds.Clear();
        foreach (var suggestion in suggestions)
        {
            var shape = suggestion.ToManualShape();
            editorShapes.Add(shape);
            autoAnnotationSuggestions.Remove(suggestion);
            selectedShapeIds.Add(shape.Id);
        }

        hoveredSuggestionId = null;
        hoveredShapeId = selectedShapeIds.FirstOrDefault();
        PushHistorySnapshot(markDirty: true);
    }

    private void ClearCurrentFrameSuggestions()
    {
        if (IsCanvasInteractionBlocked)
        {
            return;
        }

        var removed = autoAnnotationSuggestions.RemoveAll(x => x.FrameIndex == EffectiveFrameIndex);
        if (removed <= 0)
        {
            return;
        }

        hoveredSuggestionId = null;
    }

    private void ClearModelSuggestions(AutoAnnotationModelConfig model)
    {
        if (IsCanvasInteractionBlocked)
        {
            return;
        }

        var removed = AutoAnnotationSuggestionOperations.ClearModelSuggestions(autoAnnotationSuggestions, model);
        if (removed <= 0)
        {
            return;
        }

        if (hoveredSuggestionId != null && autoAnnotationSuggestions.All(x => !string.Equals(x.Id, hoveredSuggestionId, StringComparison.OrdinalIgnoreCase)))
        {
            hoveredSuggestionId = null;
        }
    }

    private void BeginRemoveCurrentFrame()
    {
        pendingRemoveFrameIndex = EffectiveFrameIndex;
    }

    private void CancelRemoveCurrentFrame()
    {
        pendingRemoveFrameIndex = -1;
    }

    private async Task RemoveCurrentFrame()
    {
        if (!CanRemoveCurrentFrame || pendingRemoveFrameIndex != EffectiveFrameIndex)
        {
            return;
        }

        try
        {
            await PersistEditorAnnotations(GetAutoSaveStatus(), clearDirty: false);
            await Project.RemoteProject.RemoveTaskFrame(TaskInfo.Id, EffectiveFrameIndex, deleteImageFile: true);
            pendingRemoveFrameIndex = -1;

            if (currentFrameIndex >= FrameCount - 1)
            {
                currentFrameIndex = Math.Max(0, currentFrameIndex - 1);
            }

            ClearImageSourceUrlCache();
            await LoadEditorData();
            currentFrameIndex = FrameCount <= 0 ? 0 : Math.Clamp(currentFrameIndex, 0, FrameCount - 1);
            LoadCurrentFrameImage(resetView: true);
            isDirty = false;
            await Project.Refresh();
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception e)
        {
            ShowError(e.Message);
        }
    }

    private void AddLatestAutoAnnotationModel()
    {
        Project.AutoAnnotation.AddLatestModel();
    }

    private async Task ImportAutoAnnotationModel()
    {
        try
        {
            var model = await Project.AutoAnnotation.ImportCustomModel();
            if (model != null)
            {
                await ValidateAutoAnnotationModel(model);
            }
        }
        catch (Exception e)
        {
            ShowError(e.Message);
        }
    }

    private void DuplicateAutoAnnotationModel(AutoAnnotationModelConfig model)
    {
        Project.AutoAnnotation.DuplicateModel(model);
    }

    private void BeginRemoveAutoAnnotationModel(AutoAnnotationModelConfig model)
    {
        pendingRemoveModelId = model.Id;
    }

    private void CancelRemoveAutoAnnotationModel()
    {
        pendingRemoveModelId = null;
    }

    private void RemoveAutoAnnotationModel(AutoAnnotationModelConfig model)
    {
        if (!string.Equals(pendingRemoveModelId, model.Id, StringComparison.OrdinalIgnoreCase))
        {
            pendingRemoveModelId = model.Id;
            return;
        }

        Project.AutoAnnotation.RemoveModel(model);
        pendingRemoveModelId = null;
    }

    private void MoveAutoAnnotationModel(AutoAnnotationModelConfig model, int delta)
    {
        Project.AutoAnnotation.MoveModel(model, delta);
    }

    private async Task ValidateAutoAnnotationModel(AutoAnnotationModelConfig model)
    {
        if (IsCanvasInteractionBlocked)
        {
            return;
        }

        isModelLoading = true;
        activeModelOperationId = model.Id;
        modelOperationProgressPercent = 5;
        modelOperationStatusText = $"Loading {model.DisplayName}...";
        model.LastStatus = AutoAnnotationModelStatus.Running;
        model.LastError = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            modelOperationProgressPercent = 20;
            modelOperationStatusText = $"Resolving {model.DisplayName}...";
            await InvokeAsync(StateHasChanged);
            if (model.SourceKind == AutoAnnotationModelSourceKind.Latest)
            {
                await Project.TrainingDataset.Refresh();
            }

            modelOperationProgressPercent = 45;
            modelOperationStatusText = $"Loading {model.DisplayName} engine...";
            await InvokeAsync(StateHasChanged);
            await Project.AutoAnnotation.ValidateModel(
                model,
                Project.TrainingDataset.TrainedModels.Items.ToArray(),
                OrderedLabels,
                CancellationToken.None);

            modelOperationProgressPercent = 100;
            modelOperationStatusText = $"{model.DisplayName}: {FormatModelStatus(model)}";
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception e)
        {
            if (model.LastStatus == AutoAnnotationModelStatus.Running)
            {
                model.LastStatus = AutoAnnotationModelStatus.LoadFailed;
                model.LastError = e.Message;
            }

            modelOperationProgressPercent = 100;
            modelOperationStatusText = $"{model.DisplayName}: failed";
            ShowError(e.Message);
        }
        finally
        {
            isModelLoading = false;
            activeModelOperationId = null;
            modelOperationProgressPercent = null;
            modelOperationStatusText = null;
            await InvokeAsync(StateHasChanged);
        }
    }

    private bool CanRunModel(AutoAnnotationModelConfig model)
    {
        return !IsCanvasInteractionBlocked && HasLabels && FrameCount > 0 && model.IsEnabled;
    }

    private static bool ShouldShowModelLoadButton(AutoAnnotationModelConfig model)
    {
        return model.LastStatus is AutoAnnotationModelStatus.NotChecked
            or AutoAnnotationModelStatus.MissingFile
            or AutoAnnotationModelStatus.UnsupportedModel
            or AutoAnnotationModelStatus.LoadFailed;
    }

    private int GetModelSuggestionCount(AutoAnnotationModelConfig model)
    {
        return autoAnnotationSuggestions.Count(x => string.Equals(x.ModelEntryId, model.Id, StringComparison.OrdinalIgnoreCase));
    }

    private Task RunFirstAutoAnnotation(bool allFrames)
    {
        return FirstRunnableModel == null ? Task.CompletedTask : RunAutoAnnotation(FirstRunnableModel, allFrames);
    }

    private async Task RunAutoAnnotation(AutoAnnotationModelConfig model, bool allFrames)
    {
        if (!CanRunModel(model))
        {
            return;
        }

        var framesToRun = BuildAutoAnnotationFrameInputs(allFrames).ToArray();
        if (framesToRun.Length <= 0)
        {
            ShowError("No frame images are available for auto-annotation.");
            return;
        }

        CancelActiveOperation(clearSelection: false);
        selectedShapeIds.Clear();
        autoAnnotationCancellationTokenSource?.Cancel();
        autoAnnotationCancellationTokenSource?.Dispose();
        autoAnnotationCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = autoAnnotationCancellationTokenSource.Token;
        isAutoAnnotating = true;
        autoAnnotationProgressPercent = 0;
        autoAnnotationStatusText = $"Auto-annotating with {model.DisplayName}...";
        activeModelOperationId = model.Id;
        modelOperationProgressPercent = 0;
        modelOperationStatusText = autoAnnotationStatusText;
        var createSuggestions = model.CreateSuggestions;
        var mergedFrames = 0;
        var replacedCount = 0;

        try
        {
            if (model.SourceKind == AutoAnnotationModelSourceKind.Latest)
            {
                await Project.TrainingDataset.Refresh();
            }

            var progress = new Progress<AutoAnnotationRunProgress>(x =>
            {
                autoAnnotationProgressPercent = x.TotalFrames <= 0 ? null : 100.0 * x.CompletedFrames / x.TotalFrames;
                autoAnnotationStatusText = $"{x.Model.DisplayName}: {x.CompletedFrames}/{x.TotalFrames} frames";
                activeModelOperationId = x.Model.Id;
                modelOperationProgressPercent = autoAnnotationProgressPercent;
                modelOperationStatusText = $"{x.Model.DisplayName}: {x.Text} ({x.CompletedFrames}/{x.TotalFrames})";
                InvokeAsync(StateHasChanged).AndForget();
            });

            var result = await Project.AutoAnnotation.Run(
                new AutoAnnotationRunRequest
                {
                    Model = model,
                    Frames = framesToRun,
                    ProjectLabels = OrderedLabels,
                    TrainedModels = Project.TrainingDataset.TrainedModels.Items.ToArray(),
                },
                progress,
                async frameResult =>
                {
                    await InvokeAsync(() =>
                    {
                        replacedCount += createSuggestions
                            ? MergeAutoAnnotationSuggestions(model, frameResult)
                            : MergeAutoAnnotationFrameResult(model, frameResult);
                        mergedFrames++;
                        StateHasChanged();
                    });
                },
                cancellationToken);

            result.ReplacedCount = replacedCount;
            lastAutoAnnotationRunResult = result;
            autoAnnotationProgressPercent = 100;
            if (createSuggestions)
            {
                model.LastRunSummary = $"{result.AddedCount} suggestions, {result.SkippedCount} skipped";
                autoAnnotationStatusText = $"{model.DisplayName}: suggested {result.AddedCount}, replaced {result.ReplacedCount}, skipped {result.SkippedCount}";
            }
            else
            {
                autoAnnotationStatusText = $"{model.DisplayName}: added {result.AddedCount}, replaced {result.ReplacedCount}, skipped {result.SkippedCount}";
            }

            modelOperationProgressPercent = 100;
            modelOperationStatusText = autoAnnotationStatusText;
            if (mergedFrames > 0 && createSuggestions)
            {
                FocusNewAutoAnnotationSuggestionsForCurrentFrame(model);
            }
            else if (mergedFrames > 0)
            {
                PushHistorySnapshot(markDirty: true);
                SelectNewAutoAnnotationsForCurrentFrame(model);
            }
        }
        catch (OperationCanceledException)
        {
            autoAnnotationStatusText = $"{model.DisplayName}: canceled after {mergedFrames} frames";
            modelOperationStatusText = autoAnnotationStatusText;
            if (mergedFrames > 0 && !createSuggestions)
            {
                PushHistorySnapshot(markDirty: true);
            }
        }
        catch (Exception e)
        {
            autoAnnotationStatusText = $"{model.DisplayName}: failed - {e.Message}";
            modelOperationProgressPercent = 100;
            modelOperationStatusText = $"{model.DisplayName}: failed";
            if (mergedFrames > 0 && !createSuggestions)
            {
                PushHistorySnapshot(markDirty: true);
            }
            ShowError(e.Message);
        }
        finally
        {
            isAutoAnnotating = false;
            autoAnnotationProgressPercent = null;
            activeModelOperationId = null;
            modelOperationProgressPercent = null;
            modelOperationStatusText = null;
            autoAnnotationCancellationTokenSource?.Dispose();
            autoAnnotationCancellationTokenSource = null;
            await InvokeAsync(StateHasChanged);
        }
    }

    private IEnumerable<AutoAnnotationFrameInput> BuildAutoAnnotationFrameInputs(bool allFrames)
    {
        var indexes = allFrames
            ? Enumerable.Range(0, FrameCount)
            : new[] { EffectiveFrameIndex };

        foreach (var frameIndex in indexes)
        {
            var frame = frames[frameIndex];
            var imageFile = ResolveFrameImageFile(frame);
            if (imageFile?.Exists != true)
            {
                continue;
            }

            yield return new AutoAnnotationFrameInput(
                frameIndex,
                imageFile,
                frame.Width > 0 ? frame.Width : imageWidth,
                frame.Height > 0 ? frame.Height : imageHeight);
        }
    }

    private int MergeAutoAnnotationFrameResult(AutoAnnotationModelConfig model, AutoAnnotationFrameResult frameResult)
    {
        var source = AutoAnnotationAccessor.CreateShapeSource(model.Id);
        var replaced = editorShapes.RemoveAll(x =>
            x.FrameIndex == frameResult.FrameIndex &&
            string.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase));

        foreach (var prediction in frameResult.Predictions)
        {
            editorShapes.Add(new EditorShape
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = CvatAnnotationShapeKind.Rectangle,
                FrameIndex = prediction.FrameIndex,
                LabelId = prediction.ProjectLabelId,
                X = prediction.BoundingBox.X,
                Y = prediction.BoundingBox.Y,
                Width = prediction.BoundingBox.Width,
                Height = prediction.BoundingBox.Height,
                RotationDegrees = 0,
                Source = prediction.Source,
                Confidence = prediction.Confidence,
            });
        }

        return replaced;
    }

    private int MergeAutoAnnotationSuggestions(AutoAnnotationModelConfig model, AutoAnnotationFrameResult frameResult)
    {
        return AutoAnnotationSuggestionOperations.ReplaceFrameSuggestions(autoAnnotationSuggestions, model, frameResult);
    }

    private void SelectNewAutoAnnotationsForCurrentFrame(AutoAnnotationModelConfig model)
    {
        var source = AutoAnnotationAccessor.CreateShapeSource(model.Id);
        var shapes = CurrentFrameShapes
            .Where(x => string.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (shapes.Length <= 0)
        {
            return;
        }

        selectedShapeIds.Clear();
        foreach (var shape in shapes)
        {
            selectedShapeIds.Add(shape.Id);
        }

        hoveredShapeId = shapes[0].Id;
        FlashShapeSelection(shapes[0].Id).AndForget();
    }

    private void FocusNewAutoAnnotationSuggestionsForCurrentFrame(AutoAnnotationModelConfig model)
    {
        var suggestions = CurrentFrameSuggestions
            .Where(x => string.Equals(x.ModelEntryId, model.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (suggestions.Length <= 0)
        {
            return;
        }

        hoveredSuggestionId = suggestions[0].Id;
        showSuggestions = true;
    }

    private void CancelAutoAnnotationRun()
    {
        autoAnnotationCancellationTokenSource?.Cancel();
    }

    private bool TryRunAutoAnnotationByShortcut(string? key, string? code, bool allFrames)
    {
        var digit = GetShortcutDigit(key, code);
        if (digit == null)
        {
            return false;
        }

        var index = digit.Value - 1;
        if (index < 0)
        {
            return false;
        }

        var models = AutoAnnotationModels;
        if (index >= models.Count)
        {
            return true;
        }

        RunAutoAnnotation(models[index], allFrames).AndForget();
        return true;
    }

    private static int? GetShortcutDigit(string? key, string? code)
    {
        if (key is { Length: 1 } && char.IsDigit(key[0]))
        {
            return key[0] - '0';
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            if (code.StartsWith("Digit", StringComparison.OrdinalIgnoreCase) &&
                code.Length == "Digit".Length + 1 &&
                char.IsDigit(code[^1]))
            {
                return code[^1] - '0';
            }

            if (code.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase) &&
                code.Length == "Numpad".Length + 1 &&
                char.IsDigit(code[^1]))
            {
                return code[^1] - '0';
            }
        }

        return null;
    }

    private static string? GetShortcutKey(KeyboardEventArgs args)
    {
        var key = args.Key?.ToUpperInvariant();
        if (key is { Length: 1 } && (char.IsAsciiLetter(key[0]) || char.IsDigit(key[0])))
        {
            return key;
        }

        return GetShortcutKeyFromCode(args.Code) ?? key;
    }

    internal static bool IsDeleteShortcut(KeyboardEventArgs args)
    {
        if (args.Key is "Delete" or "Backspace")
        {
            return true;
        }

        return !args.CtrlKey &&
               !args.MetaKey &&
               !args.AltKey &&
               GetShortcutKey(args) == "Q";
    }

    private static string? GetShortcutKeyFromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        if (code.StartsWith("Key", StringComparison.OrdinalIgnoreCase) &&
            code.Length == "Key".Length + 1 &&
            char.IsLetter(code[^1]))
        {
            return char.ToUpperInvariant(code[^1]).ToString();
        }

        if (code.StartsWith("Digit", StringComparison.OrdinalIgnoreCase) &&
            code.Length == "Digit".Length + 1 &&
            char.IsDigit(code[^1]))
        {
            return code[^1].ToString();
        }

        if (code.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase) &&
            code.Length == "Numpad".Length + 1 &&
            char.IsDigit(code[^1]))
        {
            return code[^1].ToString();
        }

        return null;
    }

    private void SetModelEnabled(AutoAnnotationModelConfig model, ChangeEventArgs args)
    {
        model.IsEnabled = args.Value is bool value && value;
    }

    private void SetModelCreateSuggestions(AutoAnnotationModelConfig model, ChangeEventArgs args)
    {
        model.CreateSuggestions = args.Value is bool value && value;
    }

    private void UpdateModelConfidence(AutoAnnotationModelConfig model, ChangeEventArgs args)
    {
        model.ConfidenceThresholdPercentage = ParseFloat(args.Value, model.ConfidenceThresholdPercentage);
    }

    private void UpdateModelIoU(AutoAnnotationModelConfig model, ChangeEventArgs args)
    {
        model.IoUThresholdPercentage = ParseFloat(args.Value, model.IoUThresholdPercentage);
    }

    private void SetAllModelMappingsEnabled(AutoAnnotationModelConfig model, bool isEnabled)
    {
        foreach (var mapping in model.LabelMappings.Items)
        {
            mapping.IsEnabled = isEnabled;
        }
    }

    private void SetMappingEnabled(AutoAnnotationModelConfig model, AutoAnnotationLabelMapping mapping, ChangeEventArgs args)
    {
        mapping.IsEnabled = args.Value is bool value && value;
    }

    private void SetMappingProjectLabel(AutoAnnotationModelConfig model, AutoAnnotationLabelMapping mapping, ChangeEventArgs args)
    {
        var labelIdText = args.Value?.ToString();
        if (!int.TryParse(labelIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var labelId))
        {
            mapping.ProjectLabelId = null;
            mapping.ProjectLabelName = null;
            return;
        }

        var label = OrderedLabels.FirstOrDefault(x => x.Id == labelId);
        mapping.ProjectLabelId = label?.Id;
        mapping.ProjectLabelName = label?.Name;
    }

    private void EnsureActiveLabel()
    {
        if (Project.RemoteProject.Labels.Items.Any(x => x.Id == activeLabelId))
        {
            return;
        }

        activeLabelId = OrderedLabels.FirstOrDefault()?.Id ?? 0;
    }

    private AnnotationLabelInfo GetLabel(int labelId)
    {
        return Project.RemoteProject.Labels.Items.FirstOrDefault(x => x.Id == labelId)
               ?? Project.RemoteProject.Labels.Items.FirstOrDefault()
               ?? new AnnotationLabelInfo
               {
                   Id = labelId,
                   Name = $"Label #{labelId}",
               };
    }

    private string GetToolClass(EditorTool tool)
    {
        return tool == activeTool ? "task-editor-tool is-active" : "task-editor-tool";
    }

    private string GetSuggestionsToolClass()
    {
        var classes = "task-editor-tool";
        if (showSuggestions)
        {
            classes += " is-active";
        }

        if (autoAnnotationSuggestions.Count > 0)
        {
            classes += " has-suggestions";
        }

        return classes;
    }

    private string GetAnnotationsToolClass()
    {
        return showAnnotations ? "task-editor-tool is-active" : "task-editor-tool";
    }

    private string GetInspectorTabClass(TaskEditorInspectorTab tab)
    {
        return activeInspectorTab == tab ? "task-editor-tab is-active" : "task-editor-tab";
    }

    private string GetModelOperationProgressClass()
    {
        var classes = "task-editor-model-progress";
        if (IsModelOperationActive)
        {
            classes += " is-active";
        }
        else
        {
            classes += " is-hidden";
        }

        if (modelOperationProgressPercent is >= 100)
        {
            classes += " is-complete";
        }

        return classes;
    }

    private string GetModelOperationProgressBarStyle()
    {
        var progress = Math.Clamp(modelOperationProgressPercent ?? 0, 0, 100);
        return $"width:{progress.ToString("F0", CultureInfo.InvariantCulture)}%;";
    }

    private string GetSourceFilterClass(TaskEditorSourceFilter filter)
    {
        return shapeSourceFilter == filter ? "is-active" : string.Empty;
    }

    private bool IsShapeVisibleInShapeList(EditorShape shape)
    {
        return shapeSourceFilter switch
        {
            TaskEditorSourceFilter.Manual => !AutoAnnotationAccessor.IsAutomaticSource(shape.Source),
            TaskEditorSourceFilter.Automatic => AutoAnnotationAccessor.IsAutomaticSource(shape.Source),
            _ => true,
        };
    }

    private string GetShapeSourceClass(EditorShape shape)
    {
        return AutoAnnotationAccessor.IsAutomaticSource(shape.Source)
            ? "task-editor-source-pill is-model"
            : "task-editor-source-pill";
    }

    private string FormatShapeSource(EditorShape shape)
    {
        var modelEntryId = AutoAnnotationAccessor.GetModelEntryIdFromSource(shape.Source);
        if (modelEntryId == null)
        {
            return "Manual";
        }

        var model = AutoAnnotationModels.FirstOrDefault(x => x.Id.Equals(modelEntryId, StringComparison.OrdinalIgnoreCase));
        var confidence = shape.Confidence == null ? string.Empty : $" {shape.Confidence.Value * 100:F0}%";
        return model == null ? $"Unknown{confidence}" : $"{model.DisplayName}{confidence}";
    }

    private string FormatSuggestionSource(AutoAnnotationSuggestion suggestion)
    {
        var model = AutoAnnotationModels.FirstOrDefault(x => x.Id.Equals(suggestion.ModelEntryId, StringComparison.OrdinalIgnoreCase));
        var modelName = model?.DisplayName ?? suggestion.ModelDisplayName;
        return string.IsNullOrWhiteSpace(modelName) ? "Suggestion" : $"{modelName} suggestion";
    }

    private static string FormatSuggestionConfidence(AutoAnnotationSuggestion suggestion)
    {
        return $"{suggestion.Confidence * 100:F0}%";
    }

    private string GetModelStatusClass(AutoAnnotationModelConfig model)
    {
        return model.LastStatus switch
        {
            AutoAnnotationModelStatus.Ready => "task-editor-model-status is-ready",
            AutoAnnotationModelStatus.Running => "task-editor-model-status is-running",
            AutoAnnotationModelStatus.NeedsMapping => "task-editor-model-status is-warning",
            AutoAnnotationModelStatus.NotChecked => "task-editor-model-status",
            _ => "task-editor-model-status is-error",
        };
    }

    private static string FormatModelStatus(AutoAnnotationModelConfig model)
    {
        return model.LastStatus switch
        {
            AutoAnnotationModelStatus.NotChecked => "Not loaded",
            AutoAnnotationModelStatus.Ready => "Ready",
            AutoAnnotationModelStatus.NeedsMapping => "Needs mapping",
            AutoAnnotationModelStatus.MissingFile => "Missing file",
            AutoAnnotationModelStatus.UnsupportedModel => "Unsupported",
            AutoAnnotationModelStatus.LoadFailed => "Load failed",
            AutoAnnotationModelStatus.LastRunFailed => "Run failed",
            AutoAnnotationModelStatus.Running => "Running",
            _ => model.LastStatus.ToString(),
        };
    }

    private static string FormatResolvedModel(AutoAnnotationModelConfig model)
    {
        if (string.IsNullOrWhiteSpace(model.LastResolvedModelPath))
        {
            return model.SourceKind == AutoAnnotationModelSourceKind.Latest ? "Latest not resolved yet" : "Not resolved yet";
        }

        var hash = string.IsNullOrWhiteSpace(model.LastResolvedModelHash)
            ? string.Empty
            : $" · {model.LastResolvedModelHash[..Math.Min(8, model.LastResolvedModelHash.Length)]}";
        return $"{Path.GetFileName(model.LastResolvedModelPath)}{hash}";
    }

    private static string FormatModelHeaderName(AutoAnnotationModelConfig model)
    {
        return (string.IsNullOrWhiteSpace(model.DisplayName) ? "Model" : model.DisplayName).TakeMidChars(16);
    }

    private static string FormatModelShortcut(int modelIndex)
    {
        return modelIndex is >= 0 and < 9 ? $"Alt+{modelIndex + 1}" : $"{modelIndex + 1}";
    }

    private string GetMappingRowClass(AutoAnnotationLabelMapping mapping)
    {
        if (!mapping.IsEnabled)
        {
            return "task-editor-model-map-row is-disabled";
        }

        var hasProjectLabel = mapping.ProjectLabelId != null &&
                              OrderedLabels.Any(x => x.Id == mapping.ProjectLabelId);
        return hasProjectLabel ? "task-editor-model-map-row" : "task-editor-model-map-row is-error";
    }

    private string GetHitLayerClass()
    {
        var classes = "task-editor-hit-layer";
        if (IsCanvasInteractionBlocked)
        {
            return $"{classes} is-blocked";
        }

        if (dragState?.Mode == DragMode.Pan)
        {
            return $"{classes} is-panning";
        }

        if (dragState?.Mode == DragMode.MoveShape)
        {
            return $"{classes} is-moving-shape";
        }

        if (dragState?.Mode == DragMode.ResizeShape)
        {
            return $"{classes} {GetHandleCursorClass(dragState.Handle)}";
        }

        if (dragState?.Mode == DragMode.RotateShape)
        {
            return $"{classes} is-rotate";
        }

        if (activeTool == EditorTool.Rectangle || activeTool == EditorTool.RectangleSelection)
        {
            return $"{classes} is-crosshair";
        }

        if (activeTool == EditorTool.FreeformSelection)
        {
            return $"{classes} is-lasso";
        }

        if (activeTool == EditorTool.Paste)
        {
            return $"{classes} is-paste";
        }

        if (activeTool == EditorTool.Pan)
        {
            return $"{classes} is-pan";
        }

        if (hoveredHandle != EditorHandle.None)
        {
            return $"{classes} {GetHandleCursorClass(hoveredHandle)}";
        }

        return string.IsNullOrWhiteSpace(hoveredShapeId) ? $"{classes} is-pan" : $"{classes} is-moving-shape";
    }

    private string GetShapeClass(EditorShape shape)
    {
        var classes = "task-editor-object-rect";
        if (AutoAnnotationAccessor.IsAutomaticSource(shape.Source))
        {
            classes += " is-model";
        }

        if (selectedShapeIds.Contains(shape.Id))
        {
            classes += " is-selected";
        }

        if (shape.Id == hoveredShapeId)
        {
            classes += " is-hovered";
        }

        if (shape.Id == flashingShapeId)
        {
            classes += " is-flashing";
        }

        return classes;
    }

    private string GetPasteShapeClass(EditorShape shape)
    {
        return $"{GetShapeClass(shape)} is-paste-preview";
    }

    private string GetSuggestionClass(AutoAnnotationSuggestion suggestion)
    {
        var classes = "task-editor-suggestion";
        if (suggestion.Id == hoveredSuggestionId)
        {
            classes += " is-hovered";
        }

        return classes;
    }

    private string GetObjectListItemClass(EditorShape shape)
    {
        var sourceClass = AutoAnnotationAccessor.IsAutomaticSource(shape.Source) ? " is-model" : string.Empty;
        if (selectedShapeIds.Contains(shape.Id))
        {
            return $"task-editor-object-row is-selected{sourceClass}";
        }

        return shape.Id == hoveredShapeId ? $"task-editor-object-row is-hovered{sourceClass}" : $"task-editor-object-row{sourceClass}";
    }

    private string GetLabelPillClass(AnnotationLabelInfo label)
    {
        var effectiveShapes = EffectiveShapes;
        var isActive = effectiveShapes.Count > 0
            ? effectiveShapes.Any(x => x.LabelId == label.Id)
            : label.Id == activeLabelId;
        return isActive ? "task-editor-label-pill is-active" : "task-editor-label-pill";
    }

    private static string GetLabelShortcutText(int labelIndex)
    {
        return labelIndex < 9 ? $"Ctrl+{labelIndex + 1}" : string.Empty;
    }

    private bool ShouldShowShapeHandles(EditorShape shape)
    {
        return !ShouldShowSelectionBounds && (selectedShapeIds.Contains(shape.Id) || shape.Id == hoveredShapeId);
    }

    private bool ShouldShowShapeDetails(EditorShape shape)
    {
        return selectedShapeIds.Contains(shape.Id) || shape.Id == hoveredShapeId;
    }

    private string GetShapeStyle(EditorShape shape, AnnotationLabelInfo label)
    {
        var screenRect = ImageToScreen(new EditorRect(shape.X, shape.Y, shape.Width, shape.Height));
        return string.Format(
            CultureInfo.InvariantCulture,
            "left:{0}px;top:{1}px;width:{2}px;height:{3}px;border-color:{4};background:{5};--shape-color:{4};--shape-caption-scale:{6};transform:rotate({7}deg);",
            screenRect.X,
            screenRect.Y,
            screenRect.Width,
            screenRect.Height,
            label.Color,
            ToRgba(label.Color, 0.22),
            GetShapeCaptionScale(),
            shape.RotationDegrees);
    }

    private string GetSuggestionStyle(AutoAnnotationSuggestion suggestion, AnnotationLabelInfo label)
    {
        var box = suggestion.BoundingBox;
        var screenRect = ImageToScreen(new EditorRect(box.X, box.Y, box.Width, box.Height));
        return string.Format(
            CultureInfo.InvariantCulture,
            "left:{0}px;top:{1}px;width:{2}px;height:{3}px;border-color:{4};background:{5};--shape-color:{4};--shape-caption-scale:{6};",
            screenRect.X,
            screenRect.Y,
            screenRect.Width,
            screenRect.Height,
            label.Color,
            ToRgba(label.Color, 0.12),
            GetShapeCaptionScale());
    }

    private string GetDraftShapeStyle()
    {
        var screenRect = draftRectangle == null ? null : ImageToScreen(draftRectangle);
        return draftRectangle == null
            ? string.Empty
            : string.Format(
                CultureInfo.InvariantCulture,
                "left:{0}px;top:{1}px;width:{2}px;height:{3}px;--shape-caption-scale:{4};",
                screenRect!.X,
                screenRect.Y,
                screenRect.Width,
                screenRect.Height,
                GetShapeCaptionScale());
    }

    private string GetSelectionMarqueeStyle()
    {
        var screenRect = selectionMarquee == null ? null : ImageToScreen(selectionMarquee);
        return selectionMarquee == null
            ? string.Empty
            : string.Format(
                CultureInfo.InvariantCulture,
                "left:{0}px;top:{1}px;width:{2}px;height:{3}px;",
                screenRect!.X,
                screenRect.Y,
                screenRect.Width,
                screenRect.Height);
    }

    private string GetFreeformSelectionPolylinePoints()
    {
        return string.Join(
            " ",
            freeformSelectionPoints.Select(point =>
            {
                var screenPoint = ImageToScreen(point);
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1}",
                    SnapScreenPixel(screenPoint.X),
                    SnapScreenPixel(screenPoint.Y));
            }));
    }

    private static double GetShapeCaptionScale()
    {
        return 1;
    }

    private string GetVerticalGuideStyle()
    {
        if (cursorImagePoint == null)
        {
            return string.Empty;
        }

        var screenPoint = ImageToScreen(cursorImagePoint);
        return string.Format(
            CultureInfo.InvariantCulture,
            "left:{0}px;top:{1}px;height:{2}px;",
            SnapScreenPixel(screenPoint.X),
            SnapScreenPixel(viewOffsetY),
            Math.Max(0, SnapScreenPixel(imageHeight * viewScale)));
    }

    private string GetHorizontalGuideStyle()
    {
        if (cursorImagePoint == null)
        {
            return string.Empty;
        }

        var screenPoint = ImageToScreen(cursorImagePoint);
        return string.Format(
            CultureInfo.InvariantCulture,
            "left:{0}px;top:{1}px;width:{2}px;",
            SnapScreenPixel(viewOffsetX),
            SnapScreenPixel(screenPoint.Y),
            Math.Max(0, SnapScreenPixel(imageWidth * viewScale)));
    }

    private string GetGuideCrossStyle()
    {
        if (cursorImagePoint == null)
        {
            return string.Empty;
        }

        var screenPoint = ImageToScreen(cursorImagePoint);
        return string.Format(
            CultureInfo.InvariantCulture,
            "left:{0}px;top:{1}px;",
            SnapScreenPixel(screenPoint.X),
            SnapScreenPixel(screenPoint.Y));
    }

    private static string GetHandleStyle(EditorHandle handle)
    {
        return handle switch
        {
            EditorHandle.NorthWest => "left:0%;top:0%;",
            EditorHandle.North => "left:50%;top:0%;",
            EditorHandle.NorthEast => "left:100%;top:0%;",
            EditorHandle.East => "left:100%;top:50%;",
            EditorHandle.SouthEast => "left:100%;top:100%;",
            EditorHandle.South => "left:50%;top:100%;",
            EditorHandle.SouthWest => "left:0%;top:100%;",
            EditorHandle.West => "left:0%;top:50%;",
            _ => string.Empty,
        };
    }

    private string GetRotateHandleClass(EditorShape shape)
    {
        var classes = "task-editor-shape-handle is-rotate";
        return shape.Id == hoveredShapeId && hoveredHandle == EditorHandle.Rotate
            ? $"{classes} is-hovered"
            : classes;
    }

    private string GetHandleClass(EditorShape shape, EditorHandle handle)
    {
        var classes = $"task-editor-shape-handle is-resize {GetHandleCursorClass(handle)}";
        return shape.Id == hoveredShapeId && hoveredHandle == handle
            ? $"{classes} is-hovered"
            : classes;
    }

    private string GetSelectionBoundsStyle()
    {
        var bounds = SelectedSelectionBounds;
        var screenRect = bounds == null ? null : ImageToScreen(new EditorRect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        return bounds == null
            ? string.Empty
            : string.Format(
                CultureInfo.InvariantCulture,
                "left:{0}px;top:{1}px;width:{2}px;height:{3}px;--shape-caption-scale:{4};",
                screenRect!.X,
                screenRect.Y,
                screenRect.Width,
                screenRect.Height,
                GetShapeCaptionScale());
    }

    private string GetSelectionRotateHandleClass()
    {
        var classes = "task-editor-shape-handle is-rotate";
        return hoveredHandle == EditorHandle.Rotate ? $"{classes} is-hovered" : classes;
    }

    private string GetSelectionHandleClass(EditorHandle handle)
    {
        var classes = $"task-editor-shape-handle is-resize {GetHandleCursorClass(handle)}";
        return hoveredHandle == handle ? $"{classes} is-hovered" : classes;
    }

    private static string GetHandleCursorClass(EditorHandle handle)
    {
        return handle switch
        {
            EditorHandle.NorthWest => "is-resize-nw",
            EditorHandle.North => "is-resize-n",
            EditorHandle.NorthEast => "is-resize-ne",
            EditorHandle.East => "is-resize-e",
            EditorHandle.SouthEast => "is-resize-se",
            EditorHandle.South => "is-resize-s",
            EditorHandle.SouthWest => "is-resize-sw",
            EditorHandle.West => "is-resize-w",
            EditorHandle.Rotate => "is-rotate",
            _ => string.Empty,
        };
    }

    private static string FormatRect(EditorShape shape)
    {
        var rotation = Math.Abs(NormalizeRotation(shape.RotationDegrees)) < 0.1
            ? string.Empty
            : $" · {NormalizeRotation(shape.RotationDegrees):F0}°";
        return $"{shape.Width:F1}x{shape.Height:F1}px{rotation}";
    }

    private static string FormatShapeLabel(AnnotationLabelInfo label)
    {
        return string.IsNullOrWhiteSpace(label.Name)
            ? $"LABEL #{label.Id}"
            : label.Name.ToUpperInvariant();
    }

    private static string FormatRect(EditorRect rectangle)
    {
        return $"{rectangle.Width:F1}x{rectangle.Height:F1}px";
    }

    private static string FormatStatus(AnnotationTaskStatus status)
    {
        return status switch
        {
            AnnotationTaskStatus.InProgress => "In progress",
            _ => status.ToString(),
        };
    }

    private static string GetStatusClass(AnnotationTaskStatus status)
    {
        return status switch
        {
            AnnotationTaskStatus.Completed => "badge text-bg-success",
            AnnotationTaskStatus.InProgress => "badge text-bg-warning",
            _ => "badge text-bg-secondary",
        };
    }

    private static string ToRgba(string? color, double alpha)
    {
        color = color?.Trim();
        if (string.IsNullOrWhiteSpace(color) || !color.StartsWith("#", StringComparison.Ordinal))
        {
            return $"rgba(40, 196, 215, {alpha.ToString(CultureInfo.InvariantCulture)})";
        }

        var hex = color.TrimStart('#');
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(x => $"{x}{x}"));
        }

        if (hex.Length != 6 ||
            !int.TryParse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !int.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !int.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return $"rgba(40, 196, 215, {alpha.ToString(CultureInfo.InvariantCulture)})";
        }

        return $"rgba({r}, {g}, {b}, {alpha.ToString(CultureInfo.InvariantCulture)})";
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static float ParseFloat(object? value, float fallback)
    {
        return value is string valueAsString &&
               float.TryParse(valueAsString, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static double NormalizeRotation(double rotationDegrees)
    {
        var normalized = rotationDegrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static double NormalizeSignedRotation(double rotationDegrees)
    {
        var normalized = (rotationDegrees + 180) % 360;
        if (normalized < 0)
        {
            normalized += 360;
        }

        return normalized - 180;
    }

    private static EditorPoint RotatePoint(EditorPoint point, EditorPoint center, double rotationDegrees)
    {
        var radians = DegreesToRadians(rotationDegrees);
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        return new EditorPoint(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }

    private static bool Intersects(EditorRect left, EditorRect right)
    {
        return left.X <= right.Right &&
               left.Right >= right.X &&
               left.Y <= right.Bottom &&
               left.Bottom >= right.Y;
    }

    private static bool IsPointInsidePolygon(EditorPoint point, IReadOnlyList<EditorPoint> polygon)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var j = i == 0 ? polygon.Count - 1 : i - 1;
            var current = polygon[i];
            var previous = polygon[j];

            if ((current.Y > point.Y) == (previous.Y > point.Y))
            {
                continue;
            }

            var intersectX = (previous.X - current.X) * (point.Y - current.Y) / (previous.Y - current.Y) + current.X;
            if (point.X < intersectX)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double Distance(EditorPoint left, EditorPoint right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double RotationDistance(double leftDegrees, double rightDegrees)
    {
        var delta = Math.Abs(NormalizeRotation(leftDegrees) - NormalizeRotation(rightDegrees));
        return Math.Min(delta, 360 - delta);
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }

    private void ShowError(string message)
    {
        NotificationService.Open(new NotificationConfig
        {
            NotificationType = NotificationType.Error,
            Duration = 8,
            Message = message,
        }).AndForget();
    }

    public enum EditorTool
    {
        Select,
        RectangleSelection,
        FreeformSelection,
        Pan,
        Rectangle,
        Paste,
    }

    public enum TaskEditorInspectorTab
    {
        Shapes,
        Labels,
        Models,
    }

    public enum TaskEditorSourceFilter
    {
        All,
        Manual,
        Automatic,
    }

    public enum FrameNavigationAction
    {
        First,
        PreviousStep,
        Previous,
        Next,
        NextStep,
        Last,
    }

    private enum DragMode
    {
        Pan,
        MoveShape,
        ResizeShape,
        RotateShape,
    }

    private enum SelectionGesture
    {
        None,
        Rectangle,
        Freeform,
    }

    private enum FrameNavigationDirection
    {
        None,
        Previous,
        Next,
    }

    public enum EditorHandle
    {
        None,
        NorthWest,
        North,
        NorthEast,
        East,
        SouthEast,
        South,
        SouthWest,
        West,
        Rotate,
    }

    public sealed record EditorPoint(double X, double Y);

    private sealed record EditorHit(EditorShape? Shape, EditorHandle Handle, bool IsGroup = false)
    {
        public static EditorHit None { get; } = new(null, EditorHandle.None);
    }

    private sealed class DragState
    {
        private DragState()
        {
        }

        public DragMode Mode { get; private init; }

        public EditorHandle Handle { get; private init; }

        public EditorPoint StartScreenPoint { get; private init; } = new(0, 0);

        public EditorPoint StartImagePoint { get; private init; } = new(0, 0);

        public double StartOffsetX { get; private init; }

        public double StartOffsetY { get; private init; }

        public IReadOnlyDictionary<string, EditorShape> StartShapes { get; private init; } = new Dictionary<string, EditorShape>();

        public SelectionBounds? StartBounds { get; private init; }

        public bool Moved { get; set; }

        public static DragState ForPan(EditorPoint screenPoint, double offsetX, double offsetY)
        {
            return new DragState
            {
                Mode = DragMode.Pan,
                StartScreenPoint = screenPoint,
                StartOffsetX = offsetX,
                StartOffsetY = offsetY,
            };
        }

        public static DragState ForShapeMove(EditorPoint screenPoint, EditorPoint imagePoint, IEnumerable<EditorShape> shapes, SelectionBounds? bounds)
        {
            return new DragState
            {
                Mode = DragMode.MoveShape,
                StartScreenPoint = screenPoint,
                StartImagePoint = imagePoint,
                StartShapes = shapes.ToDictionary(x => x.Id, x => x.Clone(), StringComparer.OrdinalIgnoreCase),
                StartBounds = bounds,
            };
        }

        public static DragState ForResize(EditorPoint screenPoint, EditorPoint imagePoint, IEnumerable<EditorShape> shapes, EditorHandle handle, SelectionBounds? bounds)
        {
            return new DragState
            {
                Mode = DragMode.ResizeShape,
                Handle = handle,
                StartScreenPoint = screenPoint,
                StartImagePoint = imagePoint,
                StartShapes = shapes.ToDictionary(x => x.Id, x => x.Clone(), StringComparer.OrdinalIgnoreCase),
                StartBounds = bounds,
            };
        }

        public static DragState ForRotate(EditorPoint screenPoint, EditorPoint imagePoint, IEnumerable<EditorShape> shapes, SelectionBounds? bounds)
        {
            return new DragState
            {
                Mode = DragMode.RotateShape,
                Handle = EditorHandle.Rotate,
                StartScreenPoint = screenPoint,
                StartImagePoint = imagePoint,
                StartShapes = shapes.ToDictionary(x => x.Id, x => x.Clone(), StringComparer.OrdinalIgnoreCase),
                StartBounds = bounds,
            };
        }
    }

    private sealed record SelectionBounds(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;

        public double Bottom => Y + Height;

        public EditorPoint Center => new(X + Width / 2.0, Y + Height / 2.0);

        public static SelectionBounds? FromShapes(IEnumerable<EditorShape> shapes)
        {
            var shapeArray = shapes.ToArray();
            if (shapeArray.Length <= 0)
            {
                return null;
            }

            var left = shapeArray.Min(x => x.X);
            var top = shapeArray.Min(x => x.Y);
            var right = shapeArray.Max(x => x.X + x.Width);
            var bottom = shapeArray.Max(x => x.Y + x.Height);
            return new SelectionBounds(left, top, Math.Max(MinShapeSize, right - left), Math.Max(MinShapeSize, bottom - top));
        }

        public bool Contains(EditorPoint imagePoint, double tolerance)
        {
            return imagePoint.X >= X - tolerance &&
                   imagePoint.X <= Right + tolerance &&
                   imagePoint.Y >= Y - tolerance &&
                   imagePoint.Y <= Bottom + tolerance;
        }

        public EditorHandle HitHandle(EditorPoint imagePoint, double tolerance, double rotateHandleOffset)
        {
            foreach (var handle in ResizeHandles)
            {
                if (Distance(imagePoint, GetHandlePoint(handle, rotateHandleOffset)) <= tolerance)
                {
                    return handle;
                }
            }

            return Distance(imagePoint, GetHandlePoint(EditorHandle.Rotate, rotateHandleOffset)) <= tolerance
                ? EditorHandle.Rotate
                : EditorHandle.None;
        }

        public SelectionBounds ResizeFromHandle(EditorHandle handle, EditorPoint imagePoint, double minSize, bool preserveAspectRatio)
        {
            var left = X;
            var right = Right;
            var top = Y;
            var bottom = Bottom;

            switch (handle)
            {
                case EditorHandle.NorthWest:
                    left = Math.Min(imagePoint.X, right - minSize);
                    top = Math.Min(imagePoint.Y, bottom - minSize);
                    break;
                case EditorHandle.North:
                    top = Math.Min(imagePoint.Y, bottom - minSize);
                    break;
                case EditorHandle.NorthEast:
                    right = Math.Max(imagePoint.X, left + minSize);
                    top = Math.Min(imagePoint.Y, bottom - minSize);
                    break;
                case EditorHandle.East:
                    right = Math.Max(imagePoint.X, left + minSize);
                    break;
                case EditorHandle.SouthEast:
                    right = Math.Max(imagePoint.X, left + minSize);
                    bottom = Math.Max(imagePoint.Y, top + minSize);
                    break;
                case EditorHandle.South:
                    bottom = Math.Max(imagePoint.Y, top + minSize);
                    break;
                case EditorHandle.SouthWest:
                    left = Math.Min(imagePoint.X, right - minSize);
                    bottom = Math.Max(imagePoint.Y, top + minSize);
                    break;
                case EditorHandle.West:
                    left = Math.Min(imagePoint.X, right - minSize);
                    break;
            }

            if (preserveAspectRatio)
            {
                PreserveAspectRatio(handle, ref left, ref right, ref top, ref bottom, minSize);
            }

            return new SelectionBounds(left, top, Math.Max(minSize, right - left), Math.Max(minSize, bottom - top));
        }

        public SelectionBounds Clamp(double maxWidth, double maxHeight, double minSize)
        {
            var width = Math.Min(Math.Max(minSize, Width), maxWidth);
            var height = Math.Min(Math.Max(minSize, Height), maxHeight);
            return new SelectionBounds(
                TaskAnnotationWindow.Clamp(X, 0, Math.Max(0, maxWidth - width)),
                TaskAnnotationWindow.Clamp(Y, 0, Math.Max(0, maxHeight - height)),
                width,
                height);
        }

        private void PreserveAspectRatio(EditorHandle handle, ref double left, ref double right, ref double top, ref double bottom, double minSize)
        {
            var aspectRatio = Math.Max(minSize, Width) / Math.Max(minSize, Height);
            var currentWidth = Math.Max(minSize, right - left);
            var currentHeight = Math.Max(minSize, bottom - top);

            if (handle is EditorHandle.East or EditorHandle.West)
            {
                var nextHeight = Math.Max(minSize, currentWidth / aspectRatio);
                var centerY = (top + bottom) / 2.0;
                top = centerY - nextHeight / 2.0;
                bottom = centerY + nextHeight / 2.0;
                return;
            }

            if (handle is EditorHandle.North or EditorHandle.South)
            {
                var nextWidth = Math.Max(minSize, currentHeight * aspectRatio);
                var centerX = (left + right) / 2.0;
                left = centerX - nextWidth / 2.0;
                right = centerX + nextWidth / 2.0;
                return;
            }

            if (currentWidth / currentHeight > aspectRatio)
            {
                currentWidth = currentHeight * aspectRatio;
            }
            else
            {
                currentHeight = currentWidth / aspectRatio;
            }

            if (handle is EditorHandle.NorthWest or EditorHandle.SouthWest)
            {
                left = right - currentWidth;
            }
            else
            {
                right = left + currentWidth;
            }

            if (handle is EditorHandle.NorthWest or EditorHandle.NorthEast)
            {
                top = bottom - currentHeight;
            }
            else
            {
                bottom = top + currentHeight;
            }
        }

        private EditorPoint GetHandlePoint(EditorHandle handle, double rotateHandleOffset)
        {
            return handle switch
            {
                EditorHandle.NorthWest => new EditorPoint(X, Y),
                EditorHandle.North => new EditorPoint(X + Width / 2.0, Y),
                EditorHandle.NorthEast => new EditorPoint(Right, Y),
                EditorHandle.East => new EditorPoint(Right, Y + Height / 2.0),
                EditorHandle.SouthEast => new EditorPoint(Right, Bottom),
                EditorHandle.South => new EditorPoint(X + Width / 2.0, Bottom),
                EditorHandle.SouthWest => new EditorPoint(X, Bottom),
                EditorHandle.West => new EditorPoint(X, Y + Height / 2.0),
                EditorHandle.Rotate => new EditorPoint(X + Width / 2.0, Y - rotateHandleOffset),
                _ => Center,
            };
        }

        private static double Distance(EditorPoint left, EditorPoint right)
        {
            var dx = left.X - right.X;
            var dy = left.Y - right.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public sealed class EditorShape
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");

        public CvatAnnotationShapeKind Kind { get; set; } = CvatAnnotationShapeKind.Rectangle;

        public int FrameIndex { get; set; }

        public int LabelId { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public double RotationDegrees { get; set; }

        public string? Source { get; set; }

        public double? Confidence { get; set; }

        public EditorPoint Center => new(X + Width / 2.0, Y + Height / 2.0);

        public EditorShape Clone()
        {
            return new EditorShape
            {
                Id = Id,
                Kind = Kind,
                FrameIndex = FrameIndex,
                LabelId = LabelId,
                X = X,
                Y = Y,
                Width = Width,
                Height = Height,
                RotationDegrees = RotationDegrees,
                Source = Source,
                Confidence = Confidence,
            };
        }

        public EditorShape CloneWithId(string id)
        {
            return new EditorShape
            {
                Id = id,
                Kind = Kind,
                FrameIndex = FrameIndex,
                LabelId = LabelId,
                X = X,
                Y = Y,
                Width = Width,
                Height = Height,
                RotationDegrees = RotationDegrees,
                Source = Source,
                Confidence = Confidence,
            };
        }

        public void CopyGeometryFrom(EditorShape other)
        {
            X = other.X;
            Y = other.Y;
            Width = other.Width;
            Height = other.Height;
            RotationDegrees = other.RotationDegrees;
        }

        public bool HasSameGeometry(EditorShape other)
        {
            return Id == other.Id &&
                   Kind == other.Kind &&
                   FrameIndex == other.FrameIndex &&
                   LabelId == other.LabelId &&
                   Math.Abs(X - other.X) < 0.001 &&
                   Math.Abs(Y - other.Y) < 0.001 &&
                   Math.Abs(Width - other.Width) < 0.001 &&
                   Math.Abs(Height - other.Height) < 0.001 &&
                   RotationDistance(RotationDegrees, other.RotationDegrees) < 0.001 &&
                   string.Equals(Source, other.Source, StringComparison.Ordinal) &&
                   NullableDoubleEquals(Confidence, other.Confidence);
        }

        public bool Contains(EditorPoint imagePoint, double tolerance)
        {
            var local = ToLocal(imagePoint);
            return local.X >= -Width / 2.0 - tolerance &&
                   local.X <= Width / 2.0 + tolerance &&
                   local.Y >= -Height / 2.0 - tolerance &&
                   local.Y <= Height / 2.0 + tolerance;
        }

        public EditorHandle HitHandle(EditorPoint imagePoint, double tolerance, double rotateHandleOffset)
        {
            foreach (var handle in ResizeHandles)
            {
                var handlePoint = LocalToImage(GetHandleLocalPoint(handle, rotateHandleOffset));
                if (Distance(imagePoint, handlePoint) <= tolerance)
                {
                    return handle;
                }
            }

            var rotatePoint = LocalToImage(GetHandleLocalPoint(EditorHandle.Rotate, rotateHandleOffset));
            return Distance(imagePoint, rotatePoint) <= tolerance ? EditorHandle.Rotate : EditorHandle.None;
        }

        public EditorShape ResizeFromHandle(EditorHandle handle, EditorPoint imagePoint, double minSize, bool preserveAspectRatio)
        {
            var localPoint = ToLocal(imagePoint);
            var left = -Width / 2.0;
            var right = Width / 2.0;
            var top = -Height / 2.0;
            var bottom = Height / 2.0;

            switch (handle)
            {
                case EditorHandle.NorthWest:
                    left = Math.Min(localPoint.X, right - minSize);
                    top = Math.Min(localPoint.Y, bottom - minSize);
                    break;
                case EditorHandle.North:
                    top = Math.Min(localPoint.Y, bottom - minSize);
                    break;
                case EditorHandle.NorthEast:
                    right = Math.Max(localPoint.X, left + minSize);
                    top = Math.Min(localPoint.Y, bottom - minSize);
                    break;
                case EditorHandle.East:
                    right = Math.Max(localPoint.X, left + minSize);
                    break;
                case EditorHandle.SouthEast:
                    right = Math.Max(localPoint.X, left + minSize);
                    bottom = Math.Max(localPoint.Y, top + minSize);
                    break;
                case EditorHandle.South:
                    bottom = Math.Max(localPoint.Y, top + minSize);
                    break;
                case EditorHandle.SouthWest:
                    left = Math.Min(localPoint.X, right - minSize);
                    bottom = Math.Max(localPoint.Y, top + minSize);
                    break;
                case EditorHandle.West:
                    left = Math.Min(localPoint.X, right - minSize);
                    break;
            }

            if (preserveAspectRatio)
            {
                PreserveAspectRatio(handle, ref left, ref right, ref top, ref bottom, minSize);
            }

            var newWidth = Math.Max(minSize, right - left);
            var newHeight = Math.Max(minSize, bottom - top);
            var localCenter = new EditorPoint((left + right) / 2.0, (top + bottom) / 2.0);
            var imageCenter = LocalToImage(localCenter);

            return new EditorShape
            {
                Id = Id,
                Kind = Kind,
                FrameIndex = FrameIndex,
                LabelId = LabelId,
                X = imageCenter.X - newWidth / 2.0,
                Y = imageCenter.Y - newHeight / 2.0,
                Width = newWidth,
                Height = newHeight,
                RotationDegrees = RotationDegrees,
                Source = Source,
                Confidence = Confidence,
            };
        }

        private void PreserveAspectRatio(EditorHandle handle, ref double left, ref double right, ref double top, ref double bottom, double minSize)
        {
            var aspectRatio = Math.Max(minSize, Width) / Math.Max(minSize, Height);
            var currentWidth = Math.Max(minSize, right - left);
            var currentHeight = Math.Max(minSize, bottom - top);

            if (handle is EditorHandle.East or EditorHandle.West)
            {
                var nextHeight = Math.Max(minSize, currentWidth / aspectRatio);
                top = -nextHeight / 2.0;
                bottom = nextHeight / 2.0;
                return;
            }

            if (handle is EditorHandle.North or EditorHandle.South)
            {
                var nextWidth = Math.Max(minSize, currentHeight * aspectRatio);
                left = -nextWidth / 2.0;
                right = nextWidth / 2.0;
                return;
            }

            if (currentWidth / currentHeight > aspectRatio)
            {
                currentWidth = currentHeight * aspectRatio;
            }
            else
            {
                currentHeight = currentWidth / aspectRatio;
            }

            if (handle is EditorHandle.NorthWest or EditorHandle.SouthWest)
            {
                left = right - currentWidth;
            }
            else
            {
                right = left + currentWidth;
            }

            if (handle is EditorHandle.NorthWest or EditorHandle.NorthEast)
            {
                top = bottom - currentHeight;
            }
            else
            {
                bottom = top + currentHeight;
            }
        }

        public void ClampUnrotatedBounds(double maxWidth, double maxHeight)
        {
            Width = Clamp(Width, MinShapeSize, maxWidth);
            Height = Clamp(Height, MinShapeSize, maxHeight);
            X = Clamp(X, 0, Math.Max(0, maxWidth - Width));
            Y = Clamp(Y, 0, Math.Max(0, maxHeight - Height));
        }

        public CvatRectangleAnnotation ToAnnotation()
        {
            return new CvatRectangleAnnotation
            {
                Kind = Kind,
                FrameIndex = FrameIndex,
                LabelId = LabelId,
                BoundingBox = new RectangleD(X, Y, Width, Height),
                RotationDegrees = NormalizeRotation(RotationDegrees),
                Source = string.IsNullOrWhiteSpace(Source) ? "manual" : Source,
            };
        }

        public static EditorShape FromAnnotation(CvatRectangleAnnotation annotation)
        {
            return new EditorShape
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = annotation.Kind,
                FrameIndex = annotation.FrameIndex,
                LabelId = annotation.LabelId,
                X = annotation.BoundingBox.X,
                Y = annotation.BoundingBox.Y,
                Width = annotation.BoundingBox.Width,
                Height = annotation.BoundingBox.Height,
                RotationDegrees = NormalizeRotation(annotation.RotationDegrees),
                Source = annotation.Source,
            };
        }

        private static bool NullableDoubleEquals(double? left, double? right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            return Math.Abs(left.Value - right.Value) < 0.001;
        }

        private EditorPoint ToLocal(EditorPoint imagePoint)
        {
            var center = Center;
            var dx = imagePoint.X - center.X;
            var dy = imagePoint.Y - center.Y;
            var radians = DegreesToRadians(-RotationDegrees);
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            return new EditorPoint(dx * cos - dy * sin, dx * sin + dy * cos);
        }

        private EditorPoint LocalToImage(EditorPoint localPoint)
        {
            var center = Center;
            var radians = DegreesToRadians(RotationDegrees);
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            return new EditorPoint(
                center.X + localPoint.X * cos - localPoint.Y * sin,
                center.Y + localPoint.X * sin + localPoint.Y * cos);
        }

        private EditorPoint GetHandleLocalPoint(EditorHandle handle, double rotateHandleOffset)
        {
            return handle switch
            {
                EditorHandle.NorthWest => new EditorPoint(-Width / 2.0, -Height / 2.0),
                EditorHandle.North => new EditorPoint(0, -Height / 2.0),
                EditorHandle.NorthEast => new EditorPoint(Width / 2.0, -Height / 2.0),
                EditorHandle.East => new EditorPoint(Width / 2.0, 0),
                EditorHandle.SouthEast => new EditorPoint(Width / 2.0, Height / 2.0),
                EditorHandle.South => new EditorPoint(0, Height / 2.0),
                EditorHandle.SouthWest => new EditorPoint(-Width / 2.0, Height / 2.0),
                EditorHandle.West => new EditorPoint(-Width / 2.0, 0),
                EditorHandle.Rotate => new EditorPoint(0, -Height / 2.0 - rotateHandleOffset),
                _ => new EditorPoint(0, 0),
            };
        }

        private static double Distance(EditorPoint left, EditorPoint right)
        {
            var dx = left.X - right.X;
            var dy = left.Y - right.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public sealed record EditorRect(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;

        public double Bottom => Y + Height;

        public static EditorRect FromPoints(EditorPoint start, EditorPoint end)
        {
            var left = Math.Min(start.X, end.X);
            var top = Math.Min(start.Y, end.Y);
            return new EditorRect(left, top, Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        }

        public EditorRect Clamp(double maxWidth, double maxHeight)
        {
            var left = ClampValue(X, 0, maxWidth);
            var top = ClampValue(Y, 0, maxHeight);
            var right = ClampValue(X + Width, 0, maxWidth);
            var bottom = ClampValue(Y + Height, 0, maxHeight);

            return new EditorRect(
                Math.Min(left, right),
                Math.Min(top, bottom),
                Math.Abs(right - left),
                Math.Abs(bottom - top));
        }

        private static double ClampValue(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
