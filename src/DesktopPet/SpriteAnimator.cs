using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using DesktopPet.Core;
using WpfApplication = System.Windows.Application;

namespace DesktopPet;

public sealed class SpriteAnimator
{
    private readonly Dictionary<PetState, IReadOnlyList<BitmapImage>> _frames = new();
    private static readonly (PetState State, string Prefix)[] Animations =
    [
        (PetState.Idle, "idle"),
        (PetState.WalkingLeft, "walk_left"),
        (PetState.WalkingRight, "walk_right"),
        (PetState.Responding, "respond"),
        (PetState.Sleeping, "sleep"),
        (PetState.Dragging, "drag")
    ];

    public SpriteAnimator()
    {
        foreach (var (state, prefix) in Animations)
        {
            var frames = new List<BitmapImage>();
            for (var index = 0; index < 300; index++)
            {
                var uri = new Uri($"/Assets/Character/{prefix}_{index:D3}.png", UriKind.Relative);
                StreamResourceInfo? resource;
                try { resource = WpfApplication.GetResourceStream(uri); }
                catch (IOException) { resource = null; }
                if (resource is null && index == 0)
                {
                    uri = new Uri($"/Assets/Character/{prefix}_{index:D2}.png", UriKind.Relative);
                    try { resource = WpfApplication.GetResourceStream(uri); }
                    catch (IOException) { resource = null; }
                }
                if (resource is null) break;
                using (resource.Stream)
                {
                    try
                    {
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = resource.Stream;
                        image.EndInit();
                        image.Freeze();
                        if (image.PixelWidth == 0 || image.PixelHeight == 0)
                            throw new InvalidDataException($"Empty frame: {uri}");
                        frames.Add(image);
                    }
                    catch (Exception error) when (error is NotSupportedException or InvalidOperationException or System.IO.FileFormatException or InvalidDataException)
                    {
                        throw new InvalidDataException($"Character frame cannot be decoded: {uri}", error);
                    }
                }
            }
            _frames[state] = frames;
        }

        HasFrames = _frames.Values.Any(frames => frames.Count > 0);
        if (HasFrames && _frames.Any(pair => pair.Value.Count == 0))
            throw new InvalidDataException("Character art is incomplete. Provide at least one PNG frame for every state.");
    }

    public bool HasFrames { get; }

    public BitmapImage Frame(PetState state, double seconds)
    {
        var frames = _frames[state];
        var rate = state == PetState.Idle ? 30 : 8;
        return frames[(int)(seconds * rate) % frames.Count];
    }
}
