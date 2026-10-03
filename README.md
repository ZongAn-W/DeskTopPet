# Desktop Pet

A native Windows desktop pet built with WPF on .NET 8. A single transparent, always-on-top
character lives on your desktop: she breathes and blinks while idle, strolls along the bottom
edge of the active monitor's working area, reacts when you click her, falls asleep when ignored,
and can be dragged between monitors. Optional text chat connects to DeepSeek when you send a
message; pet animation and movement continue to work locally without an API account.

The character art is derived from a reference photograph, but **the photograph itself is never
bundled or read at runtime**. Only the cut-out, background-free PNG frames ship with the app.

---

## Features

| Area | Behaviour |
| --- | --- |
| Idle | Breathing and blinking animation at 30 fps |
| Walking | Strolls **left or right** along the bottom of the active monitor's working area. Each stroll is a sequence of sprite clips played in order — turn to face the direction, walk, turn back to face the viewer. Only some legs carry her across the screen: leftward the walk-up and the walk cycle, rightward only the walk module; every turn and settle is acted in place. Left and right speeds can be adjusted independently from the right-click menu. A new stroll starts 2–6 s later |
| Click response | Clicking the character plays a 4.07 s sigh (blink, breath in, soft exhale), then returns to idle. Clicking a sleeping pet wakes her instead |
| Auto sleep | Enters sleep after 5 minutes with no interaction |
| Manual sleep | Sleep/wake on demand; stays asleep until you wake her |
| Drag | Hold and move the character by 8 px or more to pick her up. She stays awake while held and snaps back to the bottom edge of whichever monitor you drop her on |
| Pause | Freezes walking. A paused pet can still auto-sleep and still responds to clicks |
| Text chat | Double-click the pet, or choose 和她聊天 from her right-click menu or the tray. DeepSeek replies in a separate text window; no speech or microphone is used |
| Chat context | Keeps the last 20 successful turns for the current app session. Closing and reopening the chat keeps its context; 新对话 starts fresh, and quitting the app clears it |
| Right-click menu | Chat, pause/resume walking, sleep/wake, independent left/right speed settings, exit |
| Notification area | Tray icon with the same commands plus exit |
| Persistence | Monitor, horizontal position, paused/sleeping state, and left/right speeds are restored on next launch |
| Multi-monitor | Remembers the monitor by device name, handles negative (left-of-primary) coordinates, and converts coordinates DPI-aware so mixed-scale setups place correctly |
| Display changes | Re-clamps to the bottom edge when the display configuration changes |

The character window is 180 × 210 px, borderless, transparent, and not shown in the taskbar.
Only the face region is clickable, so the window's transparent margins stay click-through.

## Requirements

- Windows 10 or later, x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — the projects target
  `net8.0` / `net8.0-windows`. A newer SDK also builds them, since .NET 8 targeting packs are
  resolved through the installed SDK.

The published build is self-contained, so **end users need no .NET runtime at all**.

## Build, test, and run

```powershell
# Core and Windows chat integration tests (no visible window required)
dotnet test DesktopPet.sln

# Build the app
dotnet build src/DesktopPet/DesktopPet.csproj

# Run it
dotnet run --project src/DesktopPet/DesktopPet.csproj
```

Or build the whole solution at once:

```powershell
dotnet build DesktopPet.sln
```

### Publish a standalone executable

```powershell
./publish.ps1
```

This produces a self-contained, single-file `publish/win-x64-current/DesktopPet.exe` that can be copied
to any x64 Windows machine and run directly. The `publish/` directory is not tracked in git —
build it locally, or attach it to a
[GitHub Release](https://github.com/ZongAn-W/DeskTopPet/releases) instead of committing it.

### Desktop shortcut

`src/DesktopPet/Assets/DesktopPet.ico` is a character-derived icon (head-and-shoulders crop of
`idle_000.png`, built at 7 sizes down to 16×16) and is embedded in the executable via
`<ApplicationIcon>`, so the exe, taskbar and Alt-Tab all show the pet instead of the generic .NET
icon. A shortcut pointing at the published exe can use the same file for its `IconLocation`; note
that a shortcut's icon is resolved at display time, so moving or deleting the `.ico` after creating
the shortcut leaves it blank — embedding it in the exe is what makes the icon robust.

## Project layout

```
DesktopPet.sln                  Solution: UI project, core library, and tests
publish.ps1                     Self-contained win-x64 single-file publish

src/DesktopPet.Core/            Framework-independent logic (net8.0, no WPF)
  PetState.cs                   PetState plus StrollPhase (which leg of the stroll is playing)
  AnimationTiming.cs            Frame rates, loop-vs-hold, and frame indexing (unit-tested)
  PetController.cs              State machine: strolls both ways, auto-sleep, click, drag, pause
  PetGeometry.cs                Horizontal clamping, bottom-edge alignment, walk stepping
  ScreenPlacement.cs            Resolves a saved position against the current monitor list
  DipTransform.cs               Device-pixel to DIP conversion for one monitor
  PetPreferences.cs             Immutable preference record
  PreferencesStore.cs           Atomic JSON load/save with safe defaults

src/DesktopPet/                 WPF front end (net8.0-windows)
  App.xaml(.cs)                 Entry point; reports corrupt character art instead of crashing
  PetWindow.xaml(.cs)           50 ms update tick, walking, drag/drop, monitor tracking, context menu
  PetVisual.cs                  Renders sprite frames, or a vector placeholder when no art is present;
                                owns the per-state animation clock
  SpriteAnimator.cs             Loads PNG frames per state folder (timing comes from AnimationTiming)
  VideoPlayback.cs              Optional FFmpeg-backed transparent video playback with PNG fallback
  TrayController.cs             Generated tray icon and notification-area menu
  Assets/Character/             Committed PNG frames, one folder per animation
    idle/                       000.png .. 121.png
    sigh/                       000.png .. 121.png
    walk_start/                 000.png .. 061.png
    walk_left/                  000.png .. 092.png
    walk_stop/                  000.png .. 031.png
    turn_left/                  000.png .. 065.png
    turn_back/                  000.png .. 056.png
  Assets/DesktopPet.ico         Character-derived app/shortcut icon

videos/                         Source clips for optional runtime video playback and PNG
                                regeneration. Present in the working copy but git-ignored.
                                See videos/README.md
tests/DesktopPet.Core.Tests/    xUnit tests for the core library
tools/make_photo_pet.py         OpenCV/Pillow script that cut the frames out of the source photo
docs/superpowers/               Design specification and implementation plan

The behavioral walking logic lives in `PetGeometry.Step` and `PetGeometry.PickStrollTarget`, so the
tick loop in `PetWindow` stays a thin wrapper over testable math.
```

`DesktopPet.Core` deliberately has no WPF dependency, which is why all the state-machine and
placement rules are unit-testable without opening a window.

## Character frames

Frames live under `src/DesktopPet/Assets/Character/`, **one folder per animation**, and are embedded
as WPF resources. The folder name is the key the animator looks for, so adding a new animation means
dropping in a folder and adding one entry to the `Animations` table in `SpriteAnimator.cs`, plus its
rate and loop flag in `AnimationTiming` (both live in the core library, so both are tested):

| Folder | State | Frame rate | Committed frames |
| --- | --- | --- | --- |
| `idle` | Idle | 30 fps | 122 |
| `sigh` | Sighing | 30 fps | 122 |
| `walk_start` | Starting to walk | 30 fps | 62 |
| `walk_left` | Walking left | 30 fps | 93 |
| `walk_stop` | Stopping | 30 fps | 32 |
| `respond` | Click response | 8 fps | **none** |
| `sleep` | Sleeping | 8 fps | **none** |
| `drag` | Being dragged | 8 fps | **none** |
| `turn_left` | Turning to face left | 30 fps | 66 |
| `turn_back` | Turning back to face front | 30 fps | 57 |
| `turn_right` | Turning to face right | 30 fps | 95 |
| `walk_right` | Walking right | 30 fps | 159 |
| `stand_right` | Settling after a rightward walk | 30 fps | 18 |

A sequence either loops or holds its final frame, and that is decided in `AnimationTiming`
(`DesktopPet.Core`). `idle`, `walk_left` and `sleep` loop; every other state holds. Holding matters
twice over: a looping turn would jump from its finished pose back to its first frame while the
controller still believed the turn was running, and `walk_right` already contains a turn back to the
viewer, so wrapping would snap her away again mid-stroll.

`AnimationTiming` is the single source of truth for frame rate and looping, and it is unit-tested.
`SpriteAnimator` owns only the folder mapping and the image loading — it deliberately does not keep
its own copy of the timing, because duplicated rules are how the renderer and the state machine
drift apart.

**The animation clock is per state.** `PetVisual.Advance` resets its clock whenever the state
changes. This is not a detail to undo: with one shared, ever-growing counter, a one-shot sequence
computes a frame index far past its own length, clamps to its final frame, and shows a single still
image for the entire state. That is exactly what happened — the two turns and the walk-up/walk-down
each played one frozen frame, and only the looping walk looked correct because wrapping hid the
fault. `SpritePlaybackTests` now pins the behaviour down.

Inside a folder, frames are plain zero-padded numbers (`000.png`, `001.png`, …) — the folder already
says which animation they belong to, so the old `idle_000.png`-style prefix was dropped. The project
file includes them recursively (`Assets\Character\**\*.png`), and `Assets\Character\` itself holds
only subfolders, so a stray image dropped at that level is ignored rather than embedded.

The default renderer uses transparent PNG sequences. The app also supports optional runtime video
playback for the source clips through an external FFmpeg executable. WPF `MediaElement` is not used:
it mishandles the alpha channel of qtrle/ARGB video, which makes the character's hair and clothing
render as transparent holes. `VideoPlayback.cs` asks FFmpeg for BGRA frames, maps them onto the same
180 x 210 canvas, and keeps the PNG/vector path as a fallback when a clip or decoder is unavailable.

For runtime video playback, place the clips and a compatible `ffmpeg.exe` in `Assets/Video` beside
the published executable, or install FFmpeg on PATH. `publish.ps1` copies the working `videos/*.mov`
clips there when they exist; it does not bundle FFmpeg. The source clips remain external rather than
embedded in the single-file executable.

### Current art state: the leftward walk is complete

Both directions are finished and wired up. Left uses `turn_left`, `walk_start`, `walk_left`,
`walk_stop`, `turn_back`; right uses `turn_right`, `walk_right`, `stand_right`. `idle` and `sigh`
round it out. The remaining states (`respond`, `sleep`, `drag`) have no frames and fall back to the
vector placeholder drawn in `PetVisual.cs`.

**A stroll is one piece of acting cut into sprite sequences.** `PetController` runs them in order and
exposes the current leg as a `StrollPhase`. Which legs travel differs by direction:

| Phase | Sprite | Frames | Moves? |
| --- | --- | --- | --- |
| `TurningLeft` | `turn_left` | 66 | **no** |
| `Starting` | `walk_start` | 62 | yes |
| `Walking` | `walk_left`, looped | 93 × repeat count | yes |
| `Stopping` | `walk_stop` | 32 | **no** |
| `TurningBack` | `turn_back` | 57 | **no** |
| `TurningRight` | `turn_right` | 95 | **no** |
| `WalkingRight` | `walk_right` | 159 | yes |
| `StandingRight` | `stand_right` | 18 | **no** |

`PetWindow` moves the window only while `StrollPhase` reports `IsMoving`. Leftward that is `Starting`
and `Walking`; rightward only `WalkingRight` travels. The opening turn and final settle both play in
place, while the rightward walk module already contains its turn back to the viewer. The two turns
are acted in place.

**The two directions differ in one important way.** A leftward stroll repeats its walk cycle
`StrollRepeatCount` times, so its distance is tunable; a rightward stroll is always a single fixed
pass, because its walk module is a one-shot that must not be looped. Measured live on this machine:
one left stroll travels about 262 px (66 in the walk-up plus 196 in two cycles); a right stroll is
shorter because its final settle is acted in place.

**Walk distance has three knobs.** `PetController.StrollRepeatCount` sets how many leftward walk
cycles are played. The right-click menu sets leftward and rightward travel independently to `1.0`,
`1.5`, `2.0` or `3.0` DIP per timer tick. Distance is speed times the moving window, so raising
the speed or the leftward repeat count lengthens that stroll.

**The per-tick step must be accumulated, not assigned.** Assigning a fractional DIP amount to
`Window.Left` every tick looks right in the property but never reaches the screen: the window
position is snapped, so a sub-pixel step is silently dropped and the pet crawls at a fixed rate no
matter what the constant says. Measured here, `1.1` and `1.65` px/tick both travelled at exactly
21 px/s until the leftover was carried forward. `_travelRemainder` holds the fraction and the window
only moves by whole DIP. With that in place the effective rate is about **15 px/s per unit** of the
constant. The selected left/right setting is accumulated separately so fractional movement is not
lost when the window position is snapped to whole DIP values.

**Watch out for the real tick length.** The dispatcher timer asks for 50 ms but actually fires at
roughly **62 ms** here, so the speed is lower than `step / 0.05` would suggest. Tuning it by
arithmetic alone will be off by around 25%; measure it, or use the table above.

Strolls can go left or right when there is room on that side. When both sides are available, the
current scheduler prefers a leftward stroll; the rightward speed setting is used whenever a
rightward stroll starts.

**Every state must eventually be replaced with matching art.** The app tolerates missing states on
purpose — refusing to start would leave you with no pet — so a partly-animated pet is a silent
visual inconsistency rather than a crash.

> **Why the non-idle frames were deleted.** In the initial commit five states each held a single
> frame written by `tools/make_photo_pet.py`, and that script writes the same cropped *photograph*
> to all six prefixes (only `walk_left` is mirrored). Those files were byte-identical to one another
> at 40927 bytes. The result was that the pet was a chibi character while idle and switched to a raw
> photo of a real person whenever it walked, was clicked, slept, or was dragged — and it broke this
> project's rule that the source photograph is never rendered at runtime. They were removed and
> replaced with the vector fallback. Do not reintroduce them: run the script only as a starting
> point and replace every generated frame with real art.

### Regenerating frames from a clip

The `idle` and `sigh` frames are both 122-frame extractions from 640×640, 30 fps, qtrle/ARGB clips,
placed on the 180 × 210 canvas by scaling the **whole frame** uniformly by `180/640 = 0.28125`
(the art uses `0.2805`) and pasting it at offset `(0, 15)` with straight alpha compositing. That
reproduces the committed idle frames at alpha IoU 0.9986.

`turn_left` and `turn_back` come from one source clip (`左转.mov`) that contains a **~270° turnaround**,
not a 90° turn. Measured with skin-pixel visibility (high = facing the camera, ~0.04 = back of head):

| Source frames | Pose | Visibility |
| --- | --- | --- |
| 0 | front facing | 0.97 |
| ~23–27 | left profile | 0.47 |
| ~30 | turned past profile | 0.15 |
| 35–42 | back of the head (minimum at 38) | 0.035 |
| 45–65 | rotating back toward the left side | 0.16 → 0.36 |
| 65 | **split point** — left-turned pose the two arcs share | 0.36 |
| 66–80 | continuing toward the left profile | 0.36 → 0.47 |
| 80 | left profile (nose pointing left) | 0.47 |
| 81–121 | left profile → front facing | 0.47 → 0.97 |

So the arcs are cut as:

- `turn_left` = source frames **0..65** (66 frames) — front → the frame-65 pose, passing through the back
- `turn_back` = source frames **65..121** (57 frames) — the frame-65 pose → front

Both sequences share source frame 65: `turn_left/065.png` and `turn_back/000.png` are byte-identical,
so the two arcs join seamlessly. The split point is a **deliberate choice**, not a detected extremum —
source frame 80 is the cleanest left profile, while 65 sits earlier in the rotation (visibility 0.36).
Moving the split is a matter of re-running the extraction with different ranges; the frames on either
side are unaffected because each source frame maps to exactly one image.

**There is no clean 90° front-to-left arc in this clip** — the turn away from the camera reaches left
profile around frame 23–27 and then keeps going to the back, so a turn that does not show her back has
to be produced by playing `turn_back` in reverse (reversing is safe; mirroring is not, because it flips
her centre-parted hair and coat lapels).

### The left-walk set

`walk_start`, `walk_left` and `walk_stop` are three consecutive slices of **one continuous walk to the
left**, so their cuts interlock:

| Output folder | Source clip | Source frames | Frames |
| --- | --- | --- | --- |
| `walk_start` | `walk-start.mov` | 3..64 (drops a 3-frame standing hold at the start) | 62 |
| `walk_left` | `walk-loop.mov` | 0..92 (unchanged) | 93 |
| `walk_stop` | `walk-stop.mov` | 1..32 (drops frame 0) | 32 |

`walk-stop.mov`'s frame 0 is **byte-identical** to `walk-loop.mov`'s last frame, which is why only
that one frame is dropped; the `walk_start`→`walk_left` seam has no overlap (the two frames differ,
mean pixel difference 6.0). Skipping either trim would produce a one-frame stutter at the seam.

Measured properties:

- **The walk is in place** — body centroid moves ≤ 8 px over the whole clip, so horizontal travel is
  the app's job. Nothing needs compensating.
- **The stride repeats every 46 frames** (1.53 s), confirmed by a lag sweep that bottoms out sharply
  at 46 and again at 92.
- **The source duplicates every 5th frame** (byte-identical neighbours). That is an encoding artifact,
  kept so playback timing matches the source 1:1.

Two traps when redoing any of this:

- **Do not use OpenCV.** `cv2.VideoCapture` silently drops the qtrle alpha channel and returns a
  3-channel image with the transparency already flattened to black. Decode with ffmpeg instead
  (`-map 0:v:0 -f rawvideo -pix_fmt rgba -`); `pip install imageio-ffmpeg` provides a binary if
  ffmpeg is not on PATH.
- **Check the feet baseline, not just the canvas size.** Every frame must keep the bottom of the
  shoes on the same row (`y = 188` for the current art) so the pet does not bob vertically.

Rules the loader enforces:

- Frames live in `Assets/Character/<folder>/` and are numbered from 0 upward **without gaps** —
  loading stops at the first missing index. Three-digit zero padding is canonical; the two-digit
  form of frame 0 (`00.png`) is still accepted so art made under the older convention keeps loading.
- If any state has frames, the loader reports completeness via `HasFramesForAllStates`. It does
  **not** fail startup when a state is missing; that state uses the vector placeholder instead.
- Keep every image on the same 180 × 210 canvas with a stable feet baseline, no background,
  and no text or watermarks.
- After adding or renaming frame files, run a **clean** build (`dotnet clean` first). An incremental
  build does not notice that a resource file was deleted or renamed, so the stale image stays
  embedded and the loader keeps resolving the old name.

The source clips live in `videos/` — 640×640 QuickTime qtrle/ARGB clips at 30 fps. They are **not
committed**: together they are about 113 MB. The frames derived from them are fully committed, so a
clone builds and runs without them; runtime video playback is optional. To re-extract frames after
editing a clip:

```powershell
ffmpeg -i videos/idle-blink.mov -vf "format=rgba" idle_%03d.png
```

See [`videos/README.md`](videos/README.md) for the canvas-mapping convention these clips must be
processed with.

To regenerate art from a photograph, place it at `tools/reference.jpg` and run
`python tools/make_photo_pet.py` (requires `opencv-python` and `Pillow`). That script writes one
photo-derived frame per state, each into its own folder under the assets directory — it is a starting
point only, not finished art. The reference photograph is ignored by git and is never copied into the
build output.

## Configuration

### DeepSeek 文字聊天

1. 双击桌宠，或在桌宠/托盘的右键菜单中选择 **和她聊天…**。
2. 点击 **AI 设置**，填写你在 [DeepSeek 开放平台](https://platform.deepseek.com/) 创建的 API 密钥。
3. 默认模型是 `deepseek-flash`，也可填写其他受账户支持的模型。设置中可调整她的性格和聊天方式。
4. 保存后输入文字，按 Enter 发送；Shift+Enter 换行。等待时点击 **取消** 可取消请求。

聊天只有文字回复，不使用麦克风或朗读。聊天窗口显示时暂停自动散步；关闭窗口后恢复散步。
关闭窗口不会丢失本次运行中的聊天，**新对话** 或退出程序会清除上下文。聊天记录不写入磁盘。
密钥和 AI 设置通过当前 Windows 用户的 DPAPI 加密保存在
`%LocalAppData%\DesktopPet\ai-settings.bin`，不写入源码或发布文件。
请求使用 [DeepSeek 官方 Chat Completions 接口](https://api-docs.deepseek.com/api/create-chat-completion/)，
仅在用户发送时将人物设定、最近的对话和当前消息提交给 DeepSeek。
密钥错误、余额不足、请求频繁、网络失败或超时会显示提示，并保留草稿以便重试。

### Pet preferences

Preferences are stored as JSON at:

```
%LocalAppData%\DesktopPet\preferences.json
```

```json
{
  "monitorId": "\\\\.\\DISPLAY1",
  "x": 1180,
  "isPaused": false,
  "isManualSleeping": false,
  "leftWalkSpeed": 2.0,
  "rightWalkSpeed": 2.0
}
```

Writes go to a temporary file and are then moved into place, so an interrupted save cannot
corrupt the real file. If the file is missing, unreadable, or malformed, the app silently falls
back to the primary monitor and safe defaults; if the saved monitor is no longer connected, the
pet reappears on the primary display. A leading UTF-8 byte-order mark is tolerated, so a file
saved by Notepad or `Set-Content -Encoding utf8` still loads. Delete the file to reset the pet
to its defaults.

## Known limitations

- **Character art is incomplete.** Both walk directions are finished — left (`turn_left`,
  `walk_start`, `walk_left`, `walk_stop`, `turn_back`) and right (`turn_right`, `walk_right`,
  `stand_right`) — along with `idle` and `sigh`. `respond`, `sleep` and `drag` still show the vector
  placeholder. See "Current art state" above.
- **The two directions are not symmetric.** A leftward stroll repeats its walk cycle, so
  `StrollRepeatCount` tunes its distance; a rightward stroll cannot repeat its walk module and is
  therefore always one fixed length. Left also travels further because its walk-up and repeated walk
  cycle both carry her across the screen.
- **Multi-monitor at differing scale factors is unverified.** `ToMonitorArea` converts every
  monitor's rectangle with the scale factor of the monitor the window is currently on. On a desktop
  where all monitors share one scale factor this is correct, and single-monitor placement is
  correct at any scale. On a *mixed* setup (for example a 150% primary and a 100% secondary) the
  converted rectangle of the *other* monitor can be wrong, which shows up as an offset pet after
  dragging it across or after `SnapToCurrentMonitor`. `DipTransform` in the core library is
  unit-tested and ready for the per-monitor fix, but validating that fix needs real mixed-DPI
  hardware. Treat dragging between differently-scaled monitors as untested.
- **Multi-monitor dragging and tray interaction have not been verified end-to-end** on real
  hardware; only the geometry math that underlies them is covered by tests.
- Opening the right-click menu and the tray menu are Chinese-only strings; the rest of the
  documentation is English.

## Out of scope

Voice input/output, reminders, growth or leveling mechanics, multiple simultaneous characters,
and launch-at-startup are intentionally not part of this version.

## License

No license file is included yet. Until one is added, the code is all rights reserved by default.
