using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using DesktopPet.Core;
using WpfApplication = System.Windows.Application;

namespace DesktopPet;

public sealed class SpriteAnimator
{
    public const int IdleFrameRate = AnimationTiming.IdleFrameRate;

    public const int WalkFrameRate = AnimationTiming.WalkFrameRate;

    private readonly Dictionary<PetState, IReadOnlyList<BitmapImage>> _frames = new();

    /// <summary>
    /// One entry per animated state, mapping it to its <c>Assets/Character/&lt;folder&gt;</c>
    /// subfolder. Frames are numbered from 0 with no gaps.
    /// </summary>
    /// <remarks>
    /// Playback rate and looping are deliberately NOT repeated here. They live in
    /// <see cref="AnimationTiming"/>, which is the single source of truth and is unit-tested in the
    /// core test project. Duplicating them is how the renderer and the state machine drift apart.
    /// </remarks>
    private static readonly (PetState State, string Folder)[] Animations =
    [
        (PetState.Idle, "idle"),
        (PetState.Sighing, "sigh"),
        (PetState.WalkingLeft, "walk_left"),
        (PetState.TurningLeft, "turn_left"),
        (PetState.WalkStarting, "walk_start"),
        (PetState.WalkStopping, "walk_stop"),
        (PetState.TurningBack, "turn_back"),
        (PetState.WalkingRight, "walk_right"),
        (PetState.TurningRight, "turn_right"),
        (PetState.StandingRight, "stand_right"),
        (PetState.Responding, "respond"),
        (PetState.Sleeping, "sleep"),
        (PetState.Dragging, "drag")
    ];

    /// <summary>Playback rate for a state, and therefore how long its sequence takes.</summary>
    public static int FrameRateFor(PetState state) => AnimationTiming.FrameRateFor(state);

    /// <summary>True when the sequence repeats; false when it holds its final frame.</summary>
    public static bool Loops(PetState state) => AnimationTiming.Loops(state);

    public SpriteAnimator()
    {
        foreach (var (state, folder) in Animations)
        {
            var frames = new List<BitmapImage>();
            for (var index = 0; index < 300; index++)
            {
                // Three-digit names are canonical; the two-digit form of frame 0 is still accepted
                // so art produced by the older documented convention keeps loading.
                var uri = new Uri($"/Assets/Character/{folder}/{index:D3}.png", UriKind.Relative);
                StreamResourceInfo? resource;
                try { resource = WpfApplication.GetResourceStream(uri); }
                catch (IOException) { resource = null; }
                if (resource is null && index == 0)
                {
                    uri = new Uri($"/Assets/Character/{folder}/00.png", UriKind.Relative);
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

        // Deliberately NOT fatal when only some states have art. Shipping frames for some states but
        // not others is a normal work-in-progress shape, and refusing to start would leave the user
        // with no pet at all. States without frames fall back to the vector placeholder.
        HasFramesForAllStates = _frames.Values.All(frames => frames.Count > 0);
    }

    /// <summary>True when every state has at least one frame, so no state needs the placeholder.</summary>
    public bool HasFramesForAllStates { get; }

    public bool HasFramesFor(PetState state) => _frames[state].Count > 0;

    /// <summary>Number of frames loaded for a state, or 0 when it has none.</summary>
    public int FrameCountFor(PetState state) => _frames[state].Count;

    /// <summary>
    /// The frame a state shows after <paramref name="seconds"/> of that state's own elapsed time.
    /// Callers must pass time since the state began, not since the app started — see
    /// <c>PetVisual.Advance</c>, which resets its clock whenever the state changes.
    /// </summary>
    public BitmapImage Frame(PetState state, double seconds)
    {
        var frames = _frames[state];
        Debug.Assert(frames.Count > 0, $"No frames loaded for {state}; check HasFramesFor first.");
        return frames[AnimationTiming.FrameIndex(state, frames.Count, seconds)];
    }
}
