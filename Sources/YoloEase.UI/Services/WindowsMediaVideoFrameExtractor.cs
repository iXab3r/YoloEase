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
    private static readonly IFluentLog Log = typeof(WindowsMediaVideoFrameExtractor).PrepareLogger();

    public async Task<VideoFrameProbe> ProbeAsync(FileInfo inputFile, CancellationToken cancellationToken = default)
    {
        try
        {
            var clip = await CreateClipAsync(inputFile, cancellationToken);
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

            var clip = await CreateClipAsync(request.InputFile, cancellationToken);
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
            for (var i = 0; i < frameIndexes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frameIndex = frameIndexes[i];
                var frameFilePath = VideoFrameSelection.GetFrameFilePath(request.InputFile, request.OutputDirectory, frameIndex);
                if (File.Exists(frameFilePath))
                {
                    skippedFramesCount++;
                    progressReporter?.Update(i + 1, frameIndexes.Count);
                    continue;
                }

                var timestamp = VideoFrameSelection.GetFrameTimestamp(frameIndex, framesPerSecond, clip.OriginalDuration);
                await SaveThumbnailAsync(composition, timestamp, frameFilePath, cancellationToken);
                savedFramesCount++;
                Log.Debug($"Saved frame {frameIndex} into {frameFilePath}");

                progressReporter?.Update(i + 1, frameIndexes.Count);
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
            .AsTask(cancellationToken);
        return await MediaClip
            .CreateFromFileAsync(storageFile)
            .AsTask(cancellationToken);
    }

    private static async Task SaveThumbnailAsync(
        MediaComposition composition,
        TimeSpan timestamp,
        string frameFilePath,
        CancellationToken cancellationToken)
    {
        using var thumbnail = await composition
            .GetThumbnailAsync(timestamp, 0, 0, VideoFramePrecision.NearestFrame)
            .AsTask(cancellationToken);
        using var thumbnailStream = thumbnail.AsStreamForRead();
        using var image = await Image.LoadAsync(thumbnailStream, cancellationToken);
        await image.SaveAsPngAsync(frameFilePath, cancellationToken);
    }

    private static VideoFrameExtractionException CreateUnsupportedVideoException(FileInfo inputFile, Exception error)
    {
        return new VideoFrameExtractionException(
            $"Failed to decode video file '{inputFile.FullName}'. The file may be unsupported by installed Windows media codecs.",
            error);
    }
}
