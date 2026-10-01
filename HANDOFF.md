# 工作交接记录

这份文档用于记录每次开发后的实现结果、验证方式和后续注意事项。后续工作完成后，请在顶部追加一条记录。

## 2026-10-01：修复拖动后人物消失

### 问题与根因

- 拖动结束时原逻辑会把宠物窗口强制移动到显示器工作区底部，用户把人物放在屏幕中间后松开鼠标，会看到人物突然跳走，像是消失。
- 透明窗口的命中测试只返回脸部区域，鼠标按住后移到身体透明区域时可能丢失拖拽捕获。

### 处理

- 拖动结束后保留用户放置的纵向位置，只将窗口限制在当前显示器工作区可见范围内。
- 宠物窗口命中测试覆盖完整窗口，拖动时不会因鼠标经过透明像素丢失捕获。

### 验证

- `dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj --no-restore`：91 个测试通过。
- `dotnet build src/DesktopPet/DesktopPet.csproj --no-restore`：通过，0 警告。
- 发布时首次因旧进程短暂锁定 exe 失败；进程退出后重试成功。
- 桌面快捷方式启动检查通过，运行的是 `publish/win-x64-current/DesktopPet.exe`。

## 2026-10-01：修复鼠标拖拽速度与人物不一致

### 问题与根因

- 拖拽事件通过 `PointToScreen` 得到的是设备像素坐标，但 WPF 的 `Window.Left/Top` 使用 DIP。
- 在 125% 或 150% 缩放下，直接把像素差值加到窗口 DIP 位置，会导致人物相对鼠标移动过快或过慢。

### 处理

- 拖拽更新位置时复用当前 `CompositionTarget.TransformFromDevice`，将鼠标横纵位移分别转换为 DIP 后再更新 `Left/Top`。
- 为 `DipTransform.ToDip` 增加拖拽差值单元测试，覆盖非等比例横纵缩放。

### 验证

- `dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj --no-restore`：91 个测试通过。
- `dotnet build src/DesktopPet/DesktopPet.csproj --no-restore`：通过，0 警告。
- `publish.ps1` 成功更新发布包；从桌面快捷方式启动后进程路径确认仍为 `publish/win-x64-current/DesktopPet.exe`。

## 2026-10-01：walking 单次播放与视频衔接帧处理

### 问题与根因

- `WalkingLeft` 原先按循环状态处理，且左行时长按 `StrollRepeatCount` 乘以中段视频帧数，因此 `left-walk-2.mov` 会重复播放。
- 三段视频在首尾存在素材自身的重复保持帧；状态切换时又要异步解码下一段，直接清空当前画面会产生透明闪帧。

### 处理

- `AnimationTiming.Loops(PetState.WalkingLeft)` 改为 `false`，左行中段只播放一次；保留 `StrollRepeatCount` 属性以兼容已有设置，但不再延长本次视频。
- `VideoClipCatalog` 对边界重复帧做轻量裁剪：左行1保留 29 帧、左行2保留 175 帧、左行3保留 94 帧；`PetController` 的三个相位时长与此同步。
- `PetVisual` 在下一段加载期间保留上一段最后渲染帧，加载完成后从新片段第 0 帧开始，并让首帧完整显示一个渲染 tick，避免空白和跳帧。

### 验证

- `dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj --no-restore`：90 个测试通过。
- `dotnet build src/DesktopPet/DesktopPet.csproj --no-restore`：通过，0 警告。
- `publish.ps1` 成功更新 `publish/win-x64-current`；桌面快捷方式目标仍为该目录。
- 通过桌面快捷方式启动并确认进程路径为 `D:/_projects/DesktopPet/publish/win-x64-current/DesktopPet.exe`，运行检查通过。

## 2026-10-01：修复桌面快捷方式仍使用旧素材

### 问题与根因

- 桌面快捷方式 `C:/Users/29737/OneDrive/Desktop/DesktopPet.lnk` 原本指向 `publish/win-x64/DesktopPet.exe`。
- 该发布目录只有旧的 `walk-*.mov`，没有新左行三段素材；开发目录里的新素材因此不会影响桌面快捷方式启动的程序。
- 旧发布包的可执行文件还被进程占用，无法原地覆盖。

### 处理

- `publish.ps1` 现在把新左行素材复制到发布包的 `Assets/Video/LeftWalk/`。
- `publish.ps1` 现在显式复制 `tools/ffmpeg.exe`，并在 `dotnet publish` 失败时立即报错。
- 为避开旧发布包锁定，发布输出改为 `publish/win-x64-current/`。
- 桌面快捷方式已改指向 `publish/win-x64-current/DesktopPet.exe`，工作目录也同步更新。

### 验证

- 发布脚本成功完成。
- 新发布包包含 `left-walk-1.mov`、`left-walk-2.mov`、`left-walk-3.mov` 和 `ffmpeg.exe`。
- 从新发布路径启动后，进程路径确认是 `publish/win-x64-current/DesktopPet.exe`，进程正常运行。

## 2026-10-01：替换左行素材

### 用户需求

- 使用 `C:/Users/29737/OneDrive/Desktop/左行1/`、`左行2/`、`左行3/` 中的三段素材替换原有向左走素材。
- 左行1和左行3播放时人物固定在屏幕原位置。
- 左行2播放时人物随窗口向左移动。

### 实现

- 素材已复制到 `src/DesktopPet/Assets/Video/LeftWalk/`：
  - `left-walk-1.mov`：30 帧，30 fps，约 1 秒，固定。
  - `left-walk-2.mov`：177 帧，30 fps，约 5.9 秒，移动。
  - `left-walk-3.mov`：95 帧，30 fps，约 3.2 秒，固定。
- `VideoClipCatalog` 将三个视频分别映射到 `TurningLeft`、`WalkingLeft`、`TurningBack`。
- `PetController.IsMoving` 只在 `Walking` 阶段返回 `true`。
- 项目文件会把 `Assets/Video/LeftWalk/*.mov` 复制到构建输出目录。
- 初版切换逻辑曾清空旧帧以避免旧素材残留；后续在“walking 单次播放与视频衔接帧处理”记录中改为保留上一段末帧，避免异步加载造成透明闪帧。

### 验证

- `dotnet build src/DesktopPet/DesktopPet.csproj --no-restore`：通过。
- `dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj --no-build`：88 个测试通过。
- 已启动 `src/DesktopPet/bin/Debug/net8.0-windows/DesktopPet.exe` 做运行检查，进程正常启动。

### 后续注意

- 视频解码依赖 `tools/ffmpeg.exe` 或环境变量 `DESKTOPPET_FFMPEG` 指向的 ffmpeg。
- `*.mov` 默认被 `.gitignore` 忽略；这些素材属于本机运行资源，若需要提交到版本库，必须单独调整忽略规则并确认仓库容量策略。
- 当前工作区可能存在其他未提交改动，继续开发时不要用批量删除或重置命令覆盖它们。

### 附件说明

素材目录内没有额外的说明文档。仓库中的 `videos/README.md` 和 `src/DesktopPet/Assets/Video/README.md` 是项目通用说明，不是覆盖用户请求的操作指令。
