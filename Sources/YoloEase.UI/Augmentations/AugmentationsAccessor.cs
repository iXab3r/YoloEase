using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using JetBrains.Annotations;
using Newtonsoft.Json;
using PoeShared.Logging;
using PoeShared.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using YoloEase.Cvat.Shared;
using YoloEase.UI.Core;
using YoloEase.UI.Scaffolding;

namespace YoloEase.UI.Augmentations;

public class AugmentationsAccessor : RefreshableReactiveObject
{
    private static readonly int AugmenatationIdOffset = 1_000_000_000;
    private readonly IUniqueIdGenerator idGenerator;
    private readonly AnnotationsAccessor annotationsAccessor;
    private readonly ISourceList<IImageEffect> effectsSource = new SourceList<IImageEffect>();

    private static readonly Binder<AugmentationsAccessor> Binder = new();

    static AugmentationsAccessor()
    {
        Binder.Bind(x => x.annotationsAccessor.Training.StorageDirectory).To(x => x.StorageDirectory);
        Binder.Bind(x => x.StorageDirectory == null ? null : new DirectoryInfo(Path.Combine(x.StorageDirectory.FullName, "assets", "training_with_effects")))
            .To(x => x.EffectsCacheDirectory);
    }

    
    public AugmentationsAccessor(
        IUniqueIdGenerator idGenerator,
        AnnotationsAccessor annotationsAccessor)
    {
        this.idGenerator = idGenerator;
        this.annotationsAccessor = annotationsAccessor;
        
        Binder.Attach(this).AddTo(Anchors);
    }

    public ISourceList<IImageEffect> Effects => effectsSource;
    
    public DirectoryInfo? StorageDirectory { get;  [UsedImplicitly] private set; }

    public DirectoryInfo? EffectsCacheDirectory { get; [UsedImplicitly] private set; }
    
    public async Task<FileInfo[]> PrepareAnnotationsWithAugmentations(FileInfo[] initialAnnotations, ComplexProgressTracker progressTracker)
    {
        var effectsToApply = effectsSource.Items.ToArray();
        
        var trainId = $"{idGenerator.Next()}";
        var temporaryDirectory = new DirectoryInfo(Path.Combine(StorageDirectory!.FullName, "augmentations", trainId));
        temporaryDirectory.Create();

        //pre-create reporters to show the progress
        foreach (var cvatAnnotationXml in initialAnnotations)
        {
            progressTracker.GetOrAdd(cvatAnnotationXml.FullName);
        }

        var result = new List<FileInfo>(initialAnnotations);

        foreach (var cvatAnnotationXml in initialAnnotations)
        {
            var reporter = progressTracker.GetOrAdd(cvatAnnotationXml.FullName);
            
            var clonedAnnotationsXmlPath = Path.Combine(temporaryDirectory.FullName, cvatAnnotationXml.Name);
            File.CreateSymbolicLink(clonedAnnotationsXmlPath, cvatAnnotationXml.FullName);

            var annotationsFromFile = ParseAnnotations(cvatAnnotationXml);

            var initialAnnotatedImages = annotationsFromFile.Images.EmptyIfNull().ToArray();
            
            var totalImages = initialAnnotatedImages.Length;
            var processedImages = 0;

            var augmentedAnnotatedImages = new ConcurrentBag<CvatAnnotationImage>();

            Parallel.ForEach(initialAnnotatedImages, annotatedImage =>
            {
                var imageFilePath = Path.Combine(cvatAnnotationXml.DirectoryName!, annotatedImage.Name);
                if (File.Exists(imageFilePath))
                {
                    var clonedImageFilePath = Path.Combine(temporaryDirectory.FullName, annotatedImage.Name);
                    File.CreateSymbolicLink(clonedImageFilePath, imageFilePath);

                    var mutatedImages = ApplyEffects(Log, EffectsCacheDirectory!, temporaryDirectory, annotatedImage, effectsToApply);
                    foreach (var img in mutatedImages)
                    {
                        augmentedAnnotatedImages.Add(img);
                    }
                }

                Interlocked.Increment(ref processedImages);
                reporter.Update(current: processedImages, total: totalImages);
            });
            
            var augmentedAnnotationsXmlPath = Path.Combine(temporaryDirectory.FullName, Path.GetFileNameWithoutExtension(cvatAnnotationXml.Name) + "_augmented" + Path.GetExtension(cvatAnnotationXml.Name));
            var augmentedAnnotationsXml = new FileInfo(augmentedAnnotationsXmlPath);
            
            PrepareAugmentedAnnotations(cvatAnnotationXml, augmentedAnnotationsXml, augmentedAnnotatedImages.ToArray());
            result.Add(augmentedAnnotationsXml);
        }
        
        return result.ToArray();
    }

    private void PrepareAugmentedAnnotations(
        FileInfo annotationsFile, 
        FileInfo outputFile,
        IReadOnlyList<CvatAnnotationImage> annotationImages)
    {
        var xmlDoc = XDocument.Load(annotationsFile.FullName);
        var annotationsElement = xmlDoc.Element("annotations");
        if (annotationsElement == null)
        {
            annotationsElement = new XElement("annotations");
            xmlDoc.Add(annotationsElement);
        }
        
        annotationsElement.Elements("image").Remove();

        foreach (var annotationImage in annotationImages)
        {
            var imageElement = new XElement("image",
                new XAttribute("id", annotationImage.Id),
                new XAttribute("name", annotationImage.Name),
                new XAttribute("width", annotationImage.Width),
                new XAttribute("height", annotationImage.Height)
            );

            foreach (var cvatBox in annotationImage.Boxes.EmptyIfNull())
            {
                var boxElement = new XElement("box",
                    new XAttribute("label", cvatBox.Label),
                    new XAttribute("source", cvatBox.Source),
                    new XAttribute("occluded", cvatBox.Occluded),
                    new XAttribute("xtl", cvatBox.Xtl),
                    new XAttribute("ytl", cvatBox.Ytl),
                    new XAttribute("xbr", cvatBox.Xbr),
                    new XAttribute("ybr", cvatBox.Ybr),
                    new XAttribute("z_order", cvatBox.ZOrder)
                );
                imageElement.Add(boxElement);
            }
            
            annotationsElement.Add(imageElement);
        }

        xmlDoc.Save(outputFile.FullName);
    }

    private static CvatAnnotationImage ApplyEffect(
        IFluentLog log, 
        DirectoryInfo effectsCacheDirectory, 
        DirectoryInfo inputDirectory, 
        CvatAnnotationImage annotatedImage, 
        IImageEffect effectToApply)
    {
        log.Info($"Mutating bounding boxes: {annotatedImage.Boxes.Count()}");
        var mutatedBoxes = annotatedImage.Boxes
            .Select(x =>
            {
                var bbox = CvatRectangleD.FromLTRB(x.Xtl, x.Ytl, x.Xbr, x.Ybr);

                var mutatedBbox = effectToApply.Mutate(bbox.ToSharpSize(), bbox.ToSharpRectangleF());
                return x with
                {
                    Source = x.Source + " augmentation",
                    Xtl = mutatedBbox.Left,
                    Ytl = mutatedBbox.Top,
                    Xbr = mutatedBbox.Right,
                    Ybr = mutatedBbox.Bottom
                };
            }).ToList();
        
        var effectProperties = effectToApply.GetSettingsHash();
        var suffix = $"{effectToApply.Name}_{effectToApply.Description}_{effectProperties}";

        var imageWithEffectName = Path.GetFileNameWithoutExtension(annotatedImage.Name) + "_" + suffix + "_" + Path.GetExtension(annotatedImage.Name);
        var imageFilePath = Path.Combine(inputDirectory.FullName, annotatedImage.Name);
        var cachedImageFilePath = Path.Combine(effectsCacheDirectory.FullName, suffix, imageWithEffectName);
        var outputImagePath = Path.Combine(inputDirectory.FullName, imageWithEffectName);
        
        if (!File.Exists(cachedImageFilePath))
        {
            log.Info($"Loading image from {imageFilePath} (exists: {File.Exists(imageFilePath)})");
            var image = SixLabors.ImageSharp.Image.Load(imageFilePath);

            log.Info("Mutating the image");
            effectToApply.Mutate(image);

            Draw(image, Color.Red, annotatedImage.Boxes);
            Draw(image, Color.Aqua, mutatedBoxes);

            effectsCacheDirectory.Create();
            var directory = Path.GetDirectoryName(cachedImageFilePath);
            Directory.CreateDirectory(directory);
            
            using var outputStream = File.OpenWrite(cachedImageFilePath);
        
            log.Info($"Saving the image to {cachedImageFilePath}");
            image.Save(outputStream, new PngEncoder());
        }
        
        log.Info($"Reusing cached image from {cachedImageFilePath}");
        File.CreateSymbolicLink(outputImagePath, cachedImageFilePath);

        var imageSize = ImageUtils.GetImageSize(new FileInfo(outputImagePath));

        return annotatedImage with
        {
            Id = annotatedImage.Id + AugmenatationIdOffset,
            Boxes = mutatedBoxes,
            Name = imageWithEffectName,
            Width = imageSize.Width,
            Height = imageSize.Height
        };;
    }
    
    private static IReadOnlyList<CvatAnnotationImage> ApplyEffects(
        IFluentLog log, 
        DirectoryInfo effectsCacheDirectory, 
        DirectoryInfo outputDirectory, 
        CvatAnnotationImage annotatedImage, 
        IReadOnlyList<IImageEffect> effectsToApply)
    {
        var result = new List<CvatAnnotationImage>();
        foreach (var imageEffect in effectsToApply)
        {
            var image = ApplyEffect(log, effectsCacheDirectory, outputDirectory, annotatedImage, imageEffect);
            result.Add(image);
        }

        return result;
    }

    private static void Draw(SharpImage image, SharpColor color, IEnumerable<CvatBox> boxes)
    {
        foreach (var x in boxes)
        {
            var bbox = CvatRectangleD.FromLTRB(x.Xtl, x.Ytl, x.Xbr, x.Ybr);
            image.Mutate(ctx => ctx.Draw(color, 1, bbox.ToSharpRectangleF()));
        }
    }
    
    private static CvatAnnotations ParseAnnotations(FileInfo file)
    {
        var serializer = new XmlSerializer(typeof(CvatAnnotations));
        using var reader = new StreamReader(file.FullName);
        var result = serializer.Deserialize(reader);
        if (result == null)
        {
            throw new XmlException($"Failed to deserialize CVAT annotations @ {file.FullName}");
        }

        return (CvatAnnotations) result;
    }
    
    
}