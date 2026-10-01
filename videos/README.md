# Source clips

The original video clips the sprite frames were extracted from. They are kept here so the art can
be re-derived, and they are **deliberately not committed** — see the bottom of this file.

| File | Size | Resolution | Frames | Used for |
| --- | --- | --- | --- | --- |
| `idle-blink.mov` | 24.8 MB | 640×640 | 122 @ 30 fps | `Assets/Character/idle/` (122 frames) |
| `sigh.mov` | 26.7 MB | 640×640 | 122 @ 30 fps | `Assets/Character/sigh/` (122 frames) |
| `turn.mov` | 31.5 MB | 640×640 | 122 @ 30 fps | `Assets/Character/turn_left/` (66) and `turn_back/` (57) |
| `walk-start.mov` | 16.6 MB | 640×640 | 65 @ 30 fps | `Assets/Character/walk_start/` (62) |
| `walk-loop.mov` | 24.1 MB | 640×640 | 93 @ 30 fps | `Assets/Character/walk_left/` (93) |
| `walk-stop.mov` | 6.6 MB | 640×640 | 33 @ 30 fps | `Assets/Character/walk_stop/` (32) |

All are QuickTime `qtrle` (`rle `) with an `argb` colour space — a **real alpha channel**, not a black
matte — plus a silent `pcm_s16le` audio track that nothing uses.

## The left-walk set

`walk-start` / `walk-loop` / `walk-stop` are three consecutive slices of **one continuous walk to the
left**, which is why their cuts interlock rather than each standing alone:

- `walk-start` drops its first 3 frames, a static standing hold (motion is zero on frames 0–2), and
  keeps source frames 3..64.
- `walk-loop` keeps source frames 0..92 unchanged. Its first frame is *not* a repeat of
  `walk-start`'s last frame — they were verified to differ (mean pixel difference 6.0) — so no trim
  was needed at that seam.
- `walk-stop` **drops source frame 0**, because frame 0 is byte-identical to `walk-loop`'s last frame.
  That duplicate is the only true seam overlap in the set.

Measured properties, worth knowing before wiring them up:

- **The walk is in place.** The body centroid moves ≤ 8 px across the whole clip, so horizontal
  travel is the app's job, not the animation's. There is nothing to compensate for.
- **The stride repeats every 46 frames** (1.53 s) — confirmed by a lag sweep on `walk-loop`, which
  bottoms out sharply at lag 46 and again at 92. A full stride is two steps.
- **The source has duplicated frames.** Every 5th frame is byte-identical to its neighbour
  (`walk-start` 12 such repeats, `walk-loop` 18, `walk-stop` 7). This is an artifact of how the clips
  were encoded, not motion. They were kept so that playback timing stays 1:1 with the source; drop
  one frame of each pair if you ever want a ~20% smaller set with identical playback.
- **Play them at 30 fps.** At the 8 fps the older walk entries assumed, the stride would stretch to
  5.75 s and read as slow motion. `SpriteAnimator` needs these states at 30 fps.
- Only the **left** direction exists. `walk_right` has no source footage; mirroring is not a safe
  substitute here for the same reason it is not for the turns (it would flip her centre-parted hair
  and coat lapels).

## Regenerating frames

1. **Decode with ffmpeg, never OpenCV.** `cv2.VideoCapture` silently drops the qtrle alpha channel
   and hands back three channels with transparency flattened to black. ffmpeg is not on PATH here;
   `pip install imageio-ffmpeg` provides a binary, or use any ffmpeg:

   ```powershell
   ffmpeg -i videos/turn.mov -vf "format=rgba" turn_%03d.png
   ```

2. **Map onto the 180 × 210 canvas** by scaling the *whole* 640×640 frame uniformly by `0.2805`
   (Lanczos) and pasting the resulting 180×180 at offset `(0, 15)` with straight alpha compositing:

   ```python
   canvas = Image.new('RGBA', (180, 210), (0, 0, 0, 0))
   canvas.alpha_composite(frame.resize((180, 180), Image.LANCZOS), (0, 15))
   ```

   This reproduces the committed idle frames at alpha IoU 0.9986 and pixel-identical output, so it is
   the convention the whole art set follows.

3. **Check the feet baseline, not just the canvas size.** Every frame must keep the bottom of the
   shoes on the same row — `y = 188` for the current art — or the pet bobs vertically as it animates.

4. **Run a clean build afterwards** (`dotnet clean` then `dotnet build`). An incremental build does
   not notice that a resource file was added, renamed or deleted, so the old images keep being served
   and the app appears unchanged.

`turn.mov` is a **~270° turnaround**, not a 90° turn: front → left profile (~frame 25) → back of the
head (~38) → back around to the left side → front. The two arcs are cut at source frame 65, which is
why `turn_left` and `turn_back` share that frame. See the "Character frames" section of the repository
README for the measured timeline.

## Why these are not in git

Together they are about **113 MB**, roughly 93% of the working tree, so the source clips remain
ignored rather than committed. `.gitignore` excludes `videos/*.mov` (and `*.mov` generally). This
README itself **is** committed, so the folder and these instructions survive a clone even though the
clips do not.

The committed PNG frames remain the fallback: a fresh clone builds and runs without these files or
FFmpeg. To enable runtime video playback, `publish.ps1` copies these clips to `Assets/Video` beside
the executable when they are present. FFmpeg is a separate dependency and is not bundled by the
publish script.
