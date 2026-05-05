using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using PoeShared.Logging;
using PoeShared.Scaffolding;
using SixLabors.ImageSharp;
using Windows.Media.Editing;
using Windows.Storage;

namespace YoloEase.UI.Services;

/// <summary>
/// Extracts frames through Windows media codecs and writes them as ImageSharp PNG files.
/// </summary>
internal sealed class WindowsMediaVideoFrameExtractor : IVideoFrameExtractor
{
    private const int ThumbnailBatchSize = 16;

    private static readonly IFluentLog Log = typeof(WindowsMediaVideoFrameExtractor).PrepareLogger();

    public async Task<VideoFrameProbe> ProbeAsync(FileInfo inputFile, CancellationToken cancellationToken = default)
    {
        try
        {
            var clip = await CreateClipAsync(inputFile, cancellationToken).ConfigureAwait(false);
            var properties = clip.GetVideoEncodingProperties();
            var framesPerSecond = GetFramesPerSecond(properties.FrameRate.Numerator, properties.FrameRate.Denominator);
            var frameCount = EstimateFrameCount(clip.OriginalDuration, framesPerSecond);
            if (frameCount <= 0)
            {
                throw new InvalidOperationException($"Video stream has no usable frames: {inputFile.FullName}");
            }

            return new VideoFrameProbe(
                clip.OriginalDuration,
                framesPerSecond,
                frameCount,
                checked((int)properties.Width),
                checked((int)properties.Height));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw CreateUnsupportedVideoException(inputFile, e);
        }
    }

    public async Task<VideoFrameExtractionResult> ExtractAsync(
        VideoFrameExtractionRequest request,
        IProgressReporter? progressReporter = default,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(request.OutputDirectory.FullName);

            var clip = await CreateClipAsync(request.InputFile, cancellationToken).ConfigureAwait(false);
            var properties = clip.GetVideoEncodingProperties();
            var framesPerSecond = GetFramesPerSecond(properties.FrameRate.Numerator, properties.FrameRate.Denominator);
            var frameCount = EstimateFrameCount(clip.OriginalDuration, framesPerSecond);
            var frameIndexes = VideoFrameSelection.SelectIndexes(
                frameCount,
                request.StartFrameIndex,
                request.EndFrameIndex,
                request.FrameNth);

            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            var savedFramesCount = 0;
            var skippedFramesCount = 0;
            var processedFramesCount = 0;
            var framesToSave = new List<VideoFrameToSave>(frameIndexes.Count);
            for (var i = 0; i < frameIndexes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frameIndex = frameIndexes[i];
                var frameFilePath = VideoFrameSelection.GetFrameFilePath(request.InputFile, request.OutputDirectory, frameIndex);
                if (File.Exists(frameFilePath))
                {
                    skippedFramesCount++;
                    processedFramesCount++;
                    progressReporter?.Update(processedFramesCount, frameIndexes.Count);
                    continue;
                }

                var timestamp = VideoFrameSelection.GetFrameTimestamp(frameIndex, framesPerSecond, clip.OriginalDuration);
                framesToSave.Add(new VideoFrameToSave(frameIndex, timestamp, frameFilePath));
            }

            foreach (var batch in framesToSave.Chunk(ThumbnailBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var thumbnails = await composition
                    .GetThumbnailsAsync(batch.Select(x => x.Timestamp), 0, 0, VideoFramePrecision.NearestFrame)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                var thumbnailArray = thumbnails.ToArray();
                if (thumbnailArray.Length != batch.Length)
                {
                    throw new InvalidOperationException($"Expected {batch.Length} decoded thumbnails, got {thumbnailArray.Length}.");
                }

                try
                {
                    for (var i = 0; i < batch.Length; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var frame = batch[i];
                        await SaveThumbnailAsync(thumbnailArray[i], frame.FilePath, cancellationToken).ConfigureAwait(false);
                        savedFramesCount++;
                        processedFramesCount++;
                        Log.Debug($"Saved frame {frame.FrameIndex} into {frame.FilePath}");

                        progressReporter?.Update(processedFramesCount, frameIndexes.Count);
                    }
                }
                finally
                {
                    foreach (var thumbnail in thumbnailArray)
                    {
                        thumbnail.Dispose();
                    }
                }
            }

            progressReporter?.Update(100);
            Log.Debug(
                $"Video file processed, total frames: {frameCount}, selected: {frameIndexes.Count}, saved: {savedFramesCount}, skipped: {skippedFramesCount}");

            return new VideoFrameExtractionResult(
                request.OutputDirectory,
                frameIndexes.Count,
                savedFramesCount,
                skippedFramesCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw CreateUnsupportedVideoException(request.InputFile, e);
        }
    }

    internal static double GetFramesPerSecond(uint numerator, uint denominator)
    {
        return denominator == 0 ? 0d : (double)numerator / denominator;
    }

    internal static long EstimateFrameCount(TimeSpan duration, double framesPerSecond)
    {
        if (duration <= TimeSpan.Zero || framesPerSecond <= 0)
        {
            return 0;
        }

        return Math.Max(1, checked((long)Math.Round(duration.TotalSeconds * framesPerSecond, MidpointRounding.AwayFromZero)));
    }

    private static async Task<MediaClip> CreateClipAsync(FileInfo inputFile, CancellationToken cancellationToken)
    {
        inputFile.Refresh();
        if (!inputFile.Exists)
        {
            throw new FileNotFoundException($"Video file does not exist: {inputFile.FullName}", inputFile.FullName);
        }

        var storageFile = await StorageFile
            .GetFileFromPathAsync(inputFile.FullName)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        return await MediaClip
            .CreateFromFileAsync(storageFile)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task SaveThumbnailAsync(
        Windows.Storage.Streams.IRandomAccessStreamWithContentType thumbnail,
        string frameFilePath,
        CancellationToken cancellationToken)
    {
        using var thumbnailStream = thumbnail.AsStreamForRead();
        using var image = await Image.LoadAsync(thumbnailStream, cancellationToken).ConfigureAwait(false);
        await image.SaveAsPngAsync(frameFilePath, cancellationToken).ConfigureAwait(false);
    }

    private static VideoFrameExtractionException CreateUnsupportedVideoException(FileInfo inputFile, Exception error)
    {
        return new VideoFrameExtractionException(
            $"Failed to decode video file '{inputFile.FullName}'. The file may be unsupported by installed Windows media codecs.",
            error);
    }

    private readonly record struct VideoFrameToSave(long FrameIndex, TimeSpan Timestamp, string FilePath);
}
