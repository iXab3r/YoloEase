using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Newtonsoft.Json;
using PoeShared.Logging;

namespace YoloEase.UI.Yolo;

public sealed class YoloModel : DisposableReactiveObject
{
    private static readonly IFluentLog Log = typeof(YoloModel).PrepareLogger();
    private static readonly string InputTensorNameImages = "images";
    private static readonly string InputTensorNameNames = "names";
    public static readonly IReadOnlyList<WinColor> LabelColors = new[]
    {
        WinColor.Aqua,
        WinColor.Chartreuse,
        WinColor.MediumPurple,
        WinColor.Firebrick,
        WinColor.Yellow,
        WinColor.Bisque,
        WinColor.DodgerBlue,
        WinColor.Fuchsia,
        WinColor.Tomato
    };
    
    private readonly YoloModelDescription model = new();

    public YoloModel(byte[] modelData, SessionOptions sessionOptions)
    {
        Log.Debug($"Loading model: {ByteSizeLib.ByteSize.FromBytes(modelData.Length)}");
        ModelData = modelData;
        InferenceSession = new InferenceSession(modelData, sessionOptions);
        LoadLabelsFromModel();

        //inputs
        var imagesTensor = InferenceSession.InputMetadata[InputTensorNameImages];
        model.Size = new WinSize(imagesTensor.Dimensions[2], imagesTensor.Dimensions[3]);

        //output
        model.Outputs = InferenceSession.OutputMetadata.Keys.ToArray();
        var output = model.Outputs[0];
        var outputMetadata = InferenceSession.OutputMetadata[output];
        model.Dimensions = outputMetadata.Dimensions[1];
        ModelType = DetectModelType(InferenceSession);
        
        Anchors.Add(() =>
        {
            Log.Debug($"Disposing model and its resources");
            InferenceSession?.Dispose();
            Log.Debug($"Model is disposed");
        });
    }
    
    public InferenceSession InferenceSession { get; }

    public IReadOnlyList<byte> ModelData { get; }

    public YoloModelType ModelType { get; }
    
    public YoloModelDescription Description => model;
    
    private void LoadLabelsFromModel()
    {
        var labels = new List<string>();
        if (InferenceSession.ModelMetadata.CustomMetadataMap != null && InferenceSession.ModelMetadata.CustomMetadataMap.TryGetValue(InputTensorNameNames, out var namesJson))
        {
            try
            {
                var names = JsonConvert.DeserializeObject<Dictionary<string, string>>(namesJson) ?? new Dictionary<string, string>();
                names?.Values.Where(x => !string.IsNullOrEmpty(x)).ForEach(labels.Add);
                Log.Debug($"Detected following labels using 'names' metadata: {names.DumpToString()}");
            }
            catch (Exception e)
            {
                // failed to parse names
                Log.Warn($"Failed to parse names of model {model}: {namesJson}", e);
            }
        }

        if (!labels.Any())
        {
            Enumerable.Range(0, 20).Select(x => $"Unknown #{x + 1}").ForEach(labels.Add);
        }
        
        labels.Select((s, i) => new {i, s}).ToList().ForEach(item =>
        {
            var newLabel = new YoloLabel
            {
                Id = item.i,
                Name = item.s,
                Color = LabelColors[item.i % LabelColors.Count]
            };

            model.Labels.Add(newLabel);
        });
    }
    
    private static YoloModelType DetectModelType(InferenceSession session)
    {
        if (session.OutputMetadata.Count == 0)
        {
            throw new ArgumentException($"Unsupported model type, no outputs detected");
        }

        var output1 = session.OutputMetadata.Values.First();
        return session.OutputMetadata.Count switch
        {
            1 when output1.Dimensions.Length == 2 => YoloModelType.Classification,
            1 when output1.Dimensions.Length == 3 => YoloModelType.ObjectDetection,
            2 when output1.Dimensions.Length == 3 => YoloModelType.Segmentation,
            _ => throw new ArgumentException($"Unsupported model type: {new { OutputMetadataCount = session.OutputMetadata.Count, output1.Dimensions }}")
        };
    }
}