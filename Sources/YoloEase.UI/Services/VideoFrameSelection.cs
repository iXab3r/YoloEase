namespace YoloEase.UI.Services;

/// <summary>
/// Provides deterministic frame-index calculations shared by the UI and extractor.
/// </summary>
internal static class VideoFrameSelection
{
    public static NormalizedVideoFrameSelection Normalize(long frameCount, long startFrameIndex, long endFrameIndex, int frameNth)
    {
        var effectiveFrameNth = Math.Max(1, frameNth);
        if (frameCount <= 0)
        {
            return new NormalizedVideoFrameSelection(0, -1, effectiveFrameNth, 0);
        }

        var lastFrameIndex = frameCount - 1;
        var effectiveStart = Math.Clamp(startFrameIndex, 0, lastFrameIndex);
        var effectiveEnd = Math.Clamp(endFrameIndex, 0, lastFrameIndex);
        if (effectiveEnd < effectiveStart)
        {
            return new NormalizedVideoFrameSelection(effectiveStart, effectiveStart - 1, effectiveFrameNth, frameCount);
        }

        return new NormalizedVideoFrameSelection(effectiveStart, effectiveEnd, effectiveFrameNth, frameCount);
    }

    public static IReadOnlyList<long> SelectIndexes(long frameCount, long startFrameIndex, long endFrameIndex, int frameNth)
    {
        var selection = Normalize(frameCount, startFrameIndex, endFrameIndex, frameNth);
        if (selection.IsEmpty)
        {
            return Array.Empty<long>();
        }

        var frameIndexes = new List<long>(selection.ExpectedFrameCount);
        for (var frameIndex = selection.StartFrameIndex; frameIndex <= selection.EndFrameIndex; frameIndex += selection.FrameNth)
        {
            frameIndexes.Add(frameIndex);
        }

        return frameIndexes;
    }

    public static int GetExpectedFrameCount(long frameCount, long startFrameIndex, long endFrameIndex, int frameNth)
    {
        return Normalize(frameCount, startFrameIndex, endFrameIndex, frameNth).ExpectedFrameCount;
    }

    public static string GetFrameFilePath(FileInfo inputFile, DirectoryInfo outputDirectory, long frameIndex)
    {
        var inputFileName = Path.GetFileNameWithoutExtension(inputFile.FullName);
        return Path.Combine(outputDirectory.FullName, $"{inputFileName} #{frameIndex}.png");
    }

    public static TimeSpan GetFrameTimestamp(long frameIndex, double framesPerSecond, TimeSpan duration)
    {
        if (framesPerSecond <= 0)
        {
            return TimeSpan.Zero;
        }

        var timestamp = TimeSpan.FromSeconds(frameIndex / framesPerSecond);
        if (duration > TimeSpan.Zero && timestamp >= duration)
        {
            return TimeSpan.FromTicks(Math.Max(0, duration.Ticks - 1));
        }

        return timestamp;
    }
}

/// <summary>
/// Holds a clamped frame-selection range for video extraction.
/// </summary>
internal readonly record struct NormalizedVideoFrameSelection(
    long StartFrameIndex,
    long EndFrameIndex,
    int FrameNth,
    long FrameCount)
{
    public bool IsEmpty => FrameCount <= 0 || EndFrameIndex < StartFrameIndex;

    public int ExpectedFrameCount => IsEmpty ? 0 : checked((int)(((EndFrameIndex - StartFrameIndex) / FrameNth) + 1));
}
