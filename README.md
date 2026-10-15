# Desktop Pet

A native Windows desktop pet built with WPF on .NET 8. A single transparent, always-on-top
character lives on your desktop: she breathes and blinks while idle, strolls along the bottom
edge of the active monitor's working area, reacts when you click her, falls asleep when ignored,
and can be dragged between monitors. Optional text chat connects to DeepSeek for replies and
conversation-end memory summaries; pet animation and movement work locally without an API account.

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
| Text chat | Double-click uses the AI setting for either a lightweight bubble or the full chat window. Both views use the same in-memory DeepSeek session; no speech or microphone is used |
| Bubble chat | Shows the latest 3, 4, or 5 messages (5 by default). Dismissal can be click-outside, click-outside or 15 seconds idle, or about 20 seconds after a reply |
| Chat context | Keeps the last 20 successful turns for the current app session. Switching views and reopening either view keeps the same context; a new chat or quitting clears this short-term context |
| Long-term memory | Closing/dismissing chat, starting a new chat, or exiting summarizes useful facts into a local Markdown document. New chats and app restarts read these notes. Pending summaries survive network failures and restart |
| Memory management | View and search notes by topic, edit/reclassify individual notes, add or delete notes, and pause automatic recording. Manual saves discard earlier unsummarized conversations so old summaries cannot restore deleted notes |
| Emotions | Conversation changes her mood and reply style: calm, happy, sad, angry, shy or concerned. Both chat views show the shared mood; its strength develops gradually, recovers during idle time, and survives a new chat or restart |
| Right-click menu | Separate lightweight bubble chat and full chat window commands, plus pause/resume walking, sleep/wake, independent left/right speed settings, and exit |
| Notification area | Tray icon with separate lightweight bubble chat and full chat window commands plus exit |
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
  PetWindow.xaml(.cs)           Render-driven animation/travel; 50 ms scheduling, drag/drop, menus
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
| `TurningLeft` | `left-walk-1.mov` | 29 after trim | **no** |
| `Walking` | `left-walk-2.mov` | 175 after trim | yes |
| `TurningBack` | `left-walk-3.mov` | 94 after trim | **no** |
| `TurningRight` | `right-walk-1.mov` | 42 | **no** |
| `WalkingRight` | `right-walk-2.mov` | 215 | yes |
| `StandingRight` | `right-walk-3.mov` | 45 | **no** |

`PetWindow` moves only during the middle clip in either direction. Every clip plays once;
`StrollRepeatCount` remains a compatibility setting and does not repeat the leftward video.

Animation, controller time and travel share `CompositionTarget.Rendering`. The 50 ms dispatcher
timer only schedules strolls and updates tray state. Travel is elapsed seconds multiplied by speed,
so timer delays and display refresh rates do not alter the intended travel rate. The right-click
menu offers independent left/right speeds of 16, 24, 32 and 48 DIP/second, approximately preserving
the old settings' measured pace. Existing saved values 1.0, 1.5, 2.0 and 3.0 remain compatible.
`WalkMotion` carries subpixel distance forward and emits steps in physical pixels at the current
display scale, avoiding the loss of fractional movement when native window positions snap.

The appearance animation starts preloading idle and all six stroll clips. Frozen video frames
remain cached for the visual's lifetime; at most two clips decode concurrently. A phase waits for
its complete clip before its controller, movement and playback clocks advance together. Switching
back to a cached state restarts at frame zero without another FFmpeg process. Redraws are requested
only when the displayed frame changes. The source videos still contain repeated frames (roughly
24 distinct poses per second in a 30 fps stream); caching does not synthesize extra poses.

Stroll direction is sampled in proportion to the available room on each side. A side with less
than 12 DIP of room is excluded, keeping the pet away from the working-area edges.

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
关闭窗口不会丢失本次运行中的聊天，**新对话** 或退出程序会清除最近20轮的短期上下文。
密钥和 AI 设置通过当前 Windows 用户的 DPAPI 加密保存在
`%LocalAppData%\DesktopPet\ai-settings.bin`，不写入源码或发布文件。
请求使用 [DeepSeek 官方 Chat Completions 接口](https://api-docs.deepseek.com/api/create-chat-completion/)，
发送消息时将人物设定、记忆摘要、当前情绪、最近的对话和当前消息提交给 DeepSeek；对话结束时另外请求整理记忆。
密钥错误、余额不足、请求频繁、网络失败或超时会显示提示，并保留草稿以便重试。

### 人物情绪

她根据对话含义和近期上下文判断自己的反应，在同一次 DeepSeek 请求中生成回复和下一步情绪。
她的情绪不是用户心情的直接复制：例如用户倾诉困难时，她更可能担心并给予关心；引用小说台词不等于对她发火。
AI 设置中的性格仍是回复基础，情绪调整她的语气、措辞和表达强度。

| 情绪 | 回复风格 |
| --- | --- |
| 平静 | 自然、温和，直接回应当前话题 |
| 开心 | 更轻快，带一点玩笑和分享的兴致 |
| 低落 | 更安静、柔和，句子和语气有所收敛 |
| 生气 | 克制、直接，表达不悦和边界，仍保持尊重 |
| 害羞 | 略有迟疑、含蓄和柔和的回应 |
| 担心 | 更细心、认真，关注困难并提供支持 |

气泡和完整聊天窗口标题处显示同一个情绪，悬停可查看强度。
新情绪的强度最高40，同一情绪每轮最多变化20；空闲每2分钟降低1点，低于15时恢复平静。
空闲恢复在打开聊天或发送消息时重新计算。新对话只清除短期聊天，不重置情绪。
情绪在回复成功后保存到 `%LocalAppData%\DesktopPet\emotion.json`，重启后延续并计算离开期间的恢复。
请求失败、取消或被新对话作废的回复不会改变情绪；情绪文件损坏时恢复平静，不影响聊天和长期记忆。
模型使用 JSON 响应模式，界面只显示回复正文，不展示情绪元数据。正文完整但情绪字段异常时仍显示正文并沿用当前情绪；兼容纯文字、代码块，以及外层被 JSON 围栏包住的回复。
只有正文缺失或结构化回复无法读取时才自动重试一次，第二次仍失败会显示提示并恢复草稿。聊天回复上限为3072 tokens，以容纳代码和解释；服务明确报告截断时仍提示内容不完整。
情绪判断不增加一次独立的分类请求；对话结束时的记忆整理仍单独请求。

### 长期记忆

关闭完整聊天窗口、收起气泡、点击 **新对话** 或退出程序时，她会提炼这段对话的重点，
合并进 `%LocalAppData%\DesktopPet\memory.md`。气泡和完整窗口之间切换不触发整理。
整理对象是本段对话中所有成功的轮次，包括超出最近20轮上下文的早期内容；失败或取消的轮次不进入记忆。

文档记录用户明确表达的偏好、重要事情、计划和约定，合并重复信息并更新被纠正的事实。
下一次聊天会自动读取这份摘要，开始新对话或重启程序仍保留长期记忆。
完整聊天窗口右上角的文档图标打开 **记忆管理** 面板。可以按原有主题查看和搜索，
逐条修改内容或主题、添加和删除记忆；点击 **保存修改** 后写入文档，下次发送会读取。
主题在单独的输入框设置，记忆内容使用普通文字；不支持在内容中粘贴 Markdown 标题或代码围栏。
删除先留在草稿中，可以 **撤销修改**；关闭有未保存修改的面板时会提示保存或放弃。
面板中的 **打开文档** 仍可用默认程序打开 Markdown，外部编辑后点击 **重新读取**。
文档在编辑期间被后台整理或外部修改时，保存会拒绝覆盖，并保留面板草稿。

手动保存以你的修改为准，会丢弃保存前尚未整理的对话（包括正在进行的旧摘要）；
在保存前已经发出的聊天请求也不会在迟到后重新写入长期记忆。新的成功对话仍正常记录。
取消勾选 **自动记录新记忆** 可暂停新记录和待办整理，已有记忆仍会用于回复；
开关保存在本机加密设置中，重启后继续生效，重新开启时继续整理原先的待办。
查看、搜索、编辑和保存不需要 API 请求，也不创建不存在的来源或日期。
也可以在聊天中明确说“这件事不要记住”或“忘记这个偏好”，在对话结束时整理。

`memory.md` 是可编辑的 UTF-8 明文文档。为防止整理失败丢失内容，待整理的成功对话暂存于
同目录的 `memory.md.pending.json`，每段整理成功后移出待办文件；它不是永久聊天记录。
整理请求失败时保留原有摘要及待处理内容，下次结束对话、保存 AI 设置或启动程序时重试。
文件权限错误需要修复后重试；待办文件无法写入时，未整理内容只能在本次运行中保留。
退出最多等待15秒完成整理，未完成部分留待下次启动；整理会产生额外的 DeepSeek API 请求。

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

空回复会自动重试一次；情绪 JSON 模式仍为空时回退普通文本并保持当前情绪。每轮最多四次请求，可能增加等待和 API 费用。截断与内容过滤显示具体原因。失败诊断位于 %LOCALAPPDATA%\DesktopPet\emotion.json.chat-diagnostics.jsonl（不含正文或密钥，轮换保留上一份）。
