# Desktop Pet

A native Windows desktop pet built with WPF on .NET 8. A single transparent, always-on-top
character lives on your desktop: she breathes and blinks while idle, strolls along the bottom
edge of the active monitor's working area, reacts when you click her, falls asleep when ignored,
and can be dragged between monitors. Everything runs locally — no chat, no network, no accounts.

The character art is derived from a reference photograph, but **the photograph itself is never
bundled or read at runtime**. Only the cut-out, background-free PNG frames ship with the app.

---

## Features

| Area | Behaviour |
| --- | --- |
| Idle | Breathing and blinking animation at 30 fps |
| Walking | Wanders along the bottom of the active monitor's working area at ~1.1 px per 50 ms tick. Each stroll picks a random target up to ±150 px away and lasts at most 5 s; a new stroll starts 2–6 s later |
| Click response | Clicking the character plays an 800 ms reaction (raised hand, open mouth), then returns to idle |
| Auto sleep | Enters sleep after 5 minutes with no interaction |
| Manual sleep | Sleep/wake on demand; stays asleep until you wake her |
| Drag | Hold and move the character by 8 px or more to pick her up. She stays awake while held and snaps back to the bottom edge of whichever monitor you drop her on |
| Pause | Freezes walking. A paused pet can still auto-sleep and still responds to clicks |
| Right-click menu | Pause/resume walking, sleep/wake, exit |
| Notification area | Tray icon with the same commands plus exit |
| Persistence | Monitor, horizontal position, paused and sleeping state are restored on next launch |
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
# Unit tests (12 tests, no window required)
dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj

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

This produces a self-contained, single-file `publish/win-x64/DesktopPet.exe` that can be copied
to any x64 Windows machine and run directly. The `publish/` directory is not tracked in git —
build it locally, or attach it to a
[GitHub Release](https://github.com/ZongAn-W/DeskTopPet/releases) instead of committing it.

## Project layout

```
DesktopPet.sln                  Solution: UI project, core library, and tests
publish.ps1                     Self-contained win-x64 single-file publish

src/DesktopPet.Core/            Framework-independent logic (net8.0, no WPF)
  PetState.cs                   Idle / WalkingLeft / WalkingRight / Responding / Sleeping / Dragging
  PetController.cs              State machine: auto-sleep timing, click response, drag, pause, restore
  PetGeometry.cs                Horizontal clamping and bottom-edge alignment
  ScreenPlacement.cs            Resolves a saved position against the current monitor list
  PetPreferences.cs             Immutable preference record
  PreferencesStore.cs           Atomic JSON load/save with safe defaults

src/DesktopPet/                 WPF front end (net8.0-windows)
  App.xaml(.cs)                 Entry point; reports corrupt character art instead of crashing
  PetWindow.xaml(.cs)           50 ms update tick, walking, drag/drop, monitor tracking, context menu
  PetVisual.cs                  Renders sprite frames, or a vector placeholder when no art is present
  SpriteAnimator.cs             Loads and sequences PNG frames per state
  TrayController.cs             Generated tray icon and notification-area menu
  Assets/Character/             Committed PNG frames (see Assets/README.md)
  Assets/idle_blink.mov         NOT in the repository: the 24.8 MB reference clip the
                                122 idle frames were extracted from. Kept on the
                                author's disk and git-ignored

tests/DesktopPet.Core.Tests/    xUnit tests for the core library
tools/make_photo_pet.py         OpenCV/Pillow script that cut the frames out of the source photo
docs/superpowers/               Design specification and implementation plan
```

`DesktopPet.Core` deliberately has no WPF dependency, which is why all the state-machine and
placement rules are unit-testable without opening a window.

## Character frames

Frames live in `src/DesktopPet/Assets/Character/` and are embedded as WPF resources. The
animator discovers them by state prefix:

| Prefix | State | Frame rate |
| --- | --- | --- |
| `idle` | Idle | 30 fps |
| `walk_left` | Walking left | 8 fps |
| `walk_right` | Walking right | 8 fps |
| `respond` | Click response | 8 fps |
| `sleep` | Sleeping | 8 fps |
| `drag` | Being dragged | 8 fps |

Idle is played as a transparent PNG sequence rather than as video. A `MediaElement` was tried and
abandoned: WPF mishandles the alpha channel of qtrle/ARGB video, which makes the character's hair
and clothing render as transparent holes. The PNG frames carry a real alpha channel
(`Format32bppArgb`, alpha values spanning 0–255), so do not reintroduce video playback for the pet.

Rules the loader enforces:

- Number frames from index 0 upward **without gaps** — loading stops at the first missing index.
  Both `_00.png` and `_000.png` zero-padding are understood.
- If **any** state has frames, then **every** state must have at least one. Otherwise startup
  fails with a clear message rather than showing a half-animated pet.
- Keep every image on the same 180 × 210 canvas with a stable feet baseline, no background,
  and no text or watermarks.

The committed art is currently one frame per state except `idle`, which has 122 frames for the
blink cycle. With no frames present at all, the app falls back to the vector placeholder drawn
in `PetVisual.cs` and still runs.

The clip the idle frames came from, `src/DesktopPet/Assets/idle_blink.mov` (24.8 MB, QuickTime
qtrle/ARGB), is **not committed**. It is the single largest file in the project and used to make
up ~99.9% of the repository size, and nothing reads it at runtime, so it is git-ignored and kept
only on the author's disk. The 122 frames derived from it are fully committed, so a clone can
build and run without it. To re-extract frames after editing that clip:

```powershell
ffmpeg -i src/DesktopPet/Assets/idle_blink.mov -vf "format=rgba" idle_%03d.png
```

To regenerate art from a photograph, place it at `tools/reference.jpg` and run
`python tools/make_photo_pet.py` (requires `opencv-python` and `Pillow`). That script writes the
six state frames into the assets folder. The reference photograph is ignored by git and is never
copied into the build output.

## Configuration

Preferences are stored as JSON at:

```
%LocalAppData%\DesktopPet\preferences.json
```

```json
{
  "monitorId": "\\\\.\\DISPLAY1",
  "x": 1180,
  "isPaused": false,
  "isManualSleeping": false
}
```

Writes go to a temporary file and are then moved into place, so an interrupted save cannot
corrupt the real file. If the file is missing, unreadable, or malformed, the app silently falls
back to the primary monitor and safe defaults; if the saved monitor is no longer connected, the
pet reappears on the primary display. Delete the file to reset the pet to its defaults.

## Out of scope

Chat, networking, reminders, growth or leveling mechanics, multiple simultaneous characters, and
launch-at-startup are intentionally not part of this version.

## License

No license file is included yet. Until one is added, the code is all rights reserved by default.
