using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet.Core;

namespace DesktopPet;

internal sealed record VideoClipDefinition(string FileName, int SkipFrames = 0, int? MaxFrames = null);

internal static class VideoClipCatalog
{
    private static readonly IReadOnlyDictionary<PetState, VideoClipDefinition> Clips =
        new Dictionary<PetState, VideoClipDefinition>
        {
            [PetState.Idle] = new("idle-blink.mov"),
            [PetState.Sighing] = new("sigh.mov"),
            // The supplied left-walk set is three complete clips: the opening and closing
            // clips are acted in place, while the middle clip is the travelling walk.
            // The source clips contain one duplicate hold at a boundary. Keep one copy of each
            // authored pose, so the phase transition does not visibly stutter.
            [PetState.TurningLeft] = new("left-walk-1.mov", SkipFrames: 1, MaxFrames: 29),
            [PetState.WalkingLeft] = new("left-walk-2.mov", SkipFrames: 1, MaxFrames: 175),
            [PetState.TurningBack] = new("left-walk-3.mov", MaxFrames: 94),
            [PetState.TurningRight] = new("right-walk-1.mov"),
            [PetState.WalkingRight] = new("right-walk-2.mov"),
            [PetState.StandingRight] = new("right-walk-3.mov")
        };

    public static bool TryGet(PetState state, out VideoClipDefinition definition) => Clips.TryGetValue(state, out definition!);
}

internal static class VideoAssetResolver
{
    public static string? FindVideo(VideoClipDefinition definition)
    {
        foreach (var root in CandidateRoots())
        {
            var path = Path.Combine(root, definition.FileName);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static string? FindFfmpeg()
    {
        var configured = Environment.GetEnvironmentVariable("DESKTOPPET_FFMPEG");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        foreach (var root in CandidateRoots())
        {
            foreach (var relative in new[]
            {
                "ffmpeg.exe",
                Path.Combine("Assets", "Video", "ffmpeg.exe"),
                Path.Combine("tools", "ffmpeg.exe")
            })
            {
                var path = Path.Combine(root, relative);
                if (File.Exists(path)) return path;
            }
        }

        return "ffmpeg.exe";
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; current is not null && i < 8; i++, current = current.Parent)
        {
            yield return Path.Combine(current.FullName, "Assets", "Video");
            yield return Path.Combine(current.FullName, "Assets", "Video", "LeftWalk");
            yield return Path.Combine(current.FullName, "Assets", "Video", "RightWalk");
            yield return Path.Combine(current.FullName, "videos");
            yield return Path.Combine(current.FullName, "videos", "LeftWalk");
            yield return current.FullName;
        }
    }
}

internal static class VideoFrameDecoder
{
    private const int FrameWidth = 180;
    private const int SourceFrameHeight = 180;
    private const int FrameHeight = 210;
    private const int BytesPerPixel = 4;
    private const int SourceStride = FrameWidth * BytesPerPixel;

    public static Task<IReadOnlyList<BitmapSource>> LoadAsync(
        PetState state,
        CancellationToken cancellationToken,
        Action<BitmapSource>? onFirstFrame = null) => Task.Run(
            () => LoadSafely(state, cancellationToken, onFirstFrame), cancellationToken);

    public static Task<IReadOnlyList<BitmapSource>> LoadFileAsync(
        string fileName,
        CancellationToken cancellationToken,
        Action<BitmapSource>? onFirstFrame = null) => Task.Run(
            () => LoadFileSafely(fileName, cancellationToken, onFirstFrame), cancellationToken);

    private static async Task<IReadOnlyList<BitmapSource>> LoadFileSafely(
        string fileName,
        CancellationToken cancellationToken,
        Action<BitmapSource>? onFirstFrame)
    {
        try
        {
            var input = VideoAssetResolver.FindVideo(new VideoClipDefinition(fileName));
            if (input is null) return Array.Empty<BitmapSource>();
            return await LoadInput(input, cancellationToken, onFirstFrame: onFirstFrame);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Array.Empty<BitmapSource>(); }
    }

    private static async Task<IReadOnlyList<BitmapSource>> LoadSafely(
        PetState state,
        CancellationToken cancellationToken,
        Action<BitmapSource>? onFirstFrame)
    {
        try
        {
            return await Load(state, cancellationToken, onFirstFrame);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Array.Empty<BitmapSource>();
        }
    }

    private static async Task<IReadOnlyList<BitmapSource>> Load(
        PetState state,
        CancellationToken cancellationToken,
        Action<BitmapSource>? onFirstFrame)
    {
        if (!VideoClipCatalog.TryGet(state, out var definition)) return Array.Empty<BitmapSource>();
        var input = VideoAssetResolver.FindVideo(definition);
        var ffmpeg = VideoAssetResolver.FindFfmpeg();
        if (input is null || ffmpeg is null) return Array.Empty<BitmapSource>();

        return await LoadInput(input, cancellationToken, definition.SkipFrames, definition.MaxFrames, onFirstFrame);
    }

    private static async Task<IReadOnlyList<BitmapSource>> LoadInput(
        string input,
        CancellationToken cancellationToken,
        int skipFrames = 0,
        int? maxFrames = null,
        Action<BitmapSource>? onFirstFrame = null)
    {
        var ffmpeg = VideoAssetResolver.FindFfmpeg();
        if (ffmpeg is null) return Array.Empty<BitmapSource>();

        var startFrame = Math.Max(0, skipFrames);
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(input);
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add("scale=180:180:flags=lanczos,format=bgra");
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("rawvideo");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("bgra");
        startInfo.ArgumentList.Add("pipe:1");

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            return Array.Empty<BitmapSource>();
        }
        catch (InvalidOperationException)
        {
            return Array.Empty<BitmapSource>();
        }
        if (process is null) return Array.Empty<BitmapSource>();

        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var frames = new List<BitmapSource>();
        var buffer = new byte[SourceFrameHeight * SourceStride];
        try
        {
            await using var output = process.StandardOutput.BaseStream;
            var sourceIndex = 0;
            while (maxFrames is null || frames.Count < maxFrames.Value)
            {
                var read = await ReadFrame(output, buffer, cancellationToken);
                if (read != buffer.Length) break;
                if (sourceIndex++ < startFrame) continue;
                var frame = CreateBitmap(buffer);
                frames.Add(frame);
                if (frames.Count == 1) onFirstFrame?.Invoke(frame);
            }
        }
        catch (IOException)
        {
            return Array.Empty<BitmapSource>();
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException) { }
            try { await errorTask; }
            catch (OperationCanceledException) { }
        }

        return frames;
    }

    private static async Task<int> ReadFrame(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) break;
            offset += read;
        }
        return offset;
    }

    private static BitmapSource CreateBitmap(byte[] source)
    {
        var canvas = new byte[FrameWidth * FrameHeight * BytesPerPixel];
        for (var row = 0; row < SourceFrameHeight; row++)
        {
            Buffer.BlockCopy(source, row * SourceStride, canvas, (row + 15) * SourceStride, SourceStride);
        }

        var bitmap = BitmapSource.Create(
            FrameWidth,
            FrameHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            canvas,
            FrameWidth * BytesPerPixel);
        bitmap.Freeze();
        return bitmap;
    }
}
