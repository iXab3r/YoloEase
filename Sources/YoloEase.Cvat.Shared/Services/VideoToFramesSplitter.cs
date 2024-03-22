using System.Collections.Concurrent;
using Emgu.CV;
using PoeShared.Logging;
using PoeShared.Scaffolding;

namespace YoloEase.Cvat.Shared.Services;

public sealed class VideoToFramesSplitter : IVideoToFramesSplitter
{
    private static readonly IFluentLog Log = typeof(VideoToFramesSplitter).PrepareLogger();

    public VideoToFramesSplitter()
    {
    }

    public async Task<DirectoryInfo> Process(FileInfo inputFile, CancellationToken cancellationToken)
    {
        var videoFileName = Path.GetFileNameWithoutExtension(inputFile.FullName);
        var outputFolder = new DirectoryInfo(Path.Combine(inputFile.Directory!.FullName, videoFileName));
        await Process(inputFile, outputFolder, cancellationToken);
        return outputFolder;
    }

    public static async Task Process(
        FileInfo inputFile, 
        DirectoryInfo outputFolder, 
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputFolder.FullName);
        var inputFileName = Path.GetFileNameWithoutExtension(inputFile.FullName);

        EnumerateVideoFrames(inputFile, (frame, frameIdx) =>
        {
            var frameFilePath = Path.Combine(outputFolder.FullName, $"{inputFileName} #{frameIdx}.png");
            if (File.Exists(frameFilePath))
            {
                return;
            }
            frame.Save(frameFilePath);
            Log.Debug($"Saved frame {frameIdx} into {frameFilePath}");
        }, cancellationToken);
    }

    private static void EnumerateVideoFrames(
        FileInfo videoFilePath,
        Action<Mat, int> processFrame,
        CancellationToken cancellationToken)
    {
        // Load the video file
        using var videoCapture = new VideoCapture(videoFilePath.FullName);

        // Check if the video file is opened successfully
        if (!videoCapture.IsOpened)
        {
            throw new ArgumentException($"Failed to open the video file {videoFilePath}");
        }

        var tasks = new ConcurrentBag<Task>();

        var framePool = new BlockingCollection<Mat>();
        for (var i = 0; i < Environment.ProcessorCount * 2; i++)
        {
            framePool.Add(new Mat(), cancellationToken);
        }

        var savedFramesCount = 0;
        var frameIdx = -1;
        while (true)
        {
            frameIdx++;

            var frame = framePool.Take(cancellationToken);

            // Read the next frame
            if (!videoCapture.Read(frame))
            {
                break;
            }

            var saveFrame = frameIdx % 5 == 0; // replace with similarity comparison
            if (!saveFrame)
            {
                framePool.Add(frame, cancellationToken);
                continue;
            }

            var activeFrameIdx = frameIdx;
            var task = Task
                .Run(() => processFrame(frame, activeFrameIdx), cancellationToken)
                .ContinueWith(x => framePool.Add(frame, cancellationToken), cancellationToken);
            tasks.Add(task);
        }

        Task.WaitAll(tasks.ToArray(), cancellationToken);
        Log.Debug($"Video file processed, total frames: {frameIdx}, saved: {savedFramesCount} = {(double) savedFramesCount / frameIdx * 100:F2}%");
    }
}