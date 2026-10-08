# 工作交接记录

这份文档用于记录每次开发后的实现结果、验证方式和后续注意事项。后续工作完成后，请在顶部追加一条记录。

## 2026-10-05：聊天气泡改为「柔和陪伴」样式

- 气泡使用透明窗口、暖白圆角面板、轻阴影和指向人物的小尾巴；用户消息淡紫靠右，人物回复留白显示，正文仍可选择复制。
- 顶部改为「我在这里」和带提示的图标按钮；输入区整合发送键，空白时显示占位提示，发送中切换为停止图标。快捷键说明移到输入区提示，状态行只在请求中或需要提示时显示。
- 窗口高度随消息和多行草稿变化，最高520 DIP；长回复使用细滚动条。靠近屏幕右边缘时换到人物左侧，尾巴同步翻转；窗口尺寸和人物位置变化时重新定位。
- 验证：`dotnet test DesktopPet.sln --no-restore --disable-build-servers -m:1`，133项核心测试和33项 Windows 测试通过，共166项；`publish.ps1` 完成 Release 自包含发布至 `publish/win-x64-current/DesktopPet.exe`。
- 本地 WPF 布局检查覆盖默认宽度、300 DIP窄窗口、长回复、多行输入和尾巴翻转；检查图片在 `artifacts/bubble-qa/`。实际窗口内容高度从316 DIP增长到516 DIP，输入区完整可见。

## 2026-10-04: Configurable bubble and full-window chat

- `PetWindow` now owns one shared `ChatRuntime`; `ChatWindow` and `BubbleChatWindow` are presentation views over that runtime.
- AI settings choose the double-click default, bubble message count (3/4/5, default 5), and one of three dismissal rules.
- Right-click and tray menus always expose separate lightweight bubble and full-window chat commands. Switching views preserves the current in-memory session.
- Chat records remain memory-only. Existing encrypted AI settings persistence, cancellation, draft recovery, Enter/Shift+Enter behavior, and API contract remain unchanged.
- Validation: `dotnet test DesktopPet.sln --no-restore` and `dotnet build DesktopPet.sln --no-restore -c Release`.

## 2026-10-03：接入 DeepSeek 纯文字聊天

### 用户需求

- 桌宠接入 DeepSeek API，先做文字聊天；不需要朗读，只用文字回复。

### 实现

- 双击人物、桌宠右键菜单或托盘菜单中的“和她聊天…”打开聊天窗口。Enter 发送，Shift+Enter 换行；支持取消请求、新对话和 AI 设置。
- 使用官方 `https://api.deepseek.com/chat/completions`，默认模型 `deepseek-flash`，关闭思考输出与流式输出，只展示最终文字回答。模型和人物性格可在设置窗口中修改。
- 成功的对话保留最近20轮上下文；请求失败或取消不会提交到上下文，并恢复草稿。关闭聊天窗口会隐藏窗口并取消请求，重新打开仍保留本次运行中的对话；新对话或退出清除上下文，不保存聊天记录到磁盘。
- API 密钥及设置整体通过当前 Windows 用户的 DPAPI 加密，保存到 `%LocalAppData%/DesktopPet/ai-settings.bin`。不在源码、发布包或日志中保存明文密钥。
- 聊天显示时暂停自动散步并唤醒人物，关闭后重新安排散步。程序退出时关闭聊天并取消请求；保留此前右行素材和屏幕位置方向概率的修改。
- 没有语音输出、朗读或麦克风功能。只在用户发送消息时联网。

### 验证

- `dotnet test DesktopPet.sln --no-restore --disable-build-servers -m:1`：123项核心测试与6项 Windows 集成测试通过，共129项，0失败。
- 新测试覆盖鉴权和请求格式、最终回答、常见 API 错误、取消、上下文提交及上限；本机集成测试覆盖加密保存、设置损坏恢复、发送、取消和错误后的草稿恢复。
- 已渲染并检查聊天默认尺寸、最小尺寸和设置窗口布局，检查图片位于 `artifacts/chat-qa/`。
- Release 自包含单文件发布成功。已通过安全重启脚本更新 `publish/win-x64-current/DesktopPet.exe`，新进程43128正常响应，安装后的可执行文件 SHA-256 与已验证构建一致。

### 后续注意

- 尚未提供真实 DeepSeek 密钥，因此只用模拟 HTTP 响应验证交互，没有执行真实云端对话。用户在“AI 设置”保存有效密钥后可发送消息验证。
- 官方接口和模型以 https://api-docs.deepseek.com/ 的当前文档为准；模型名称可手动修改。

## 2026-10-03：根据屏幕位置调整行走方向概率

### 用户需求

- 人物在屏幕左侧时提高向右移动的概率，在屏幕右侧时提高向左移动的概率。

### 实现

- `PetGeometry.PickStrollDirection` 根据当前显示器工作区和人物宽度计算两侧可移动空间，并按空间比例随机选择方向。
- 可移动范围25%位置的向右概率为75%，中间左右各50%，75%位置的向左概率为75%。
- 保留12 DIP的最小移动空间；一侧空间不足时只选择另一侧，两侧都不足时暂不开始行走。
- `PetWindow` 在每次准备行走时使用最新位置和 `Random.Shared.NextDouble()` 选择方向。

### 验证

- 先用旧的固定左向优先规则运行新测试，6项位置概率用例按预期失败；改为按位置选择后，全部110项测试通过。
- 16项新用例覆盖方向分布、屏幕中央、边缘、负坐标显示器、不同宽度、人物超出边界和无移动空间。
- `dotnet build src/DesktopPet/DesktopPet.csproj --no-restore`：通过，0警告、0错误。
- 已完成Release发布，正常退出旧桌宠并更新 `publish/win-x64-current/DesktopPet.exe`；可执行文件哈希与已验证构建一致，新进程正常响应。

## 2026-10-02：替换右行三段素材

### 用户需求

- 使用桌面 `右行1/右行1-1.mov`、`右行2/右行2-1.mov`、`右行3/右行3-1.mov` 替换原右行素材。
- 右行1、右行3原地播放；只有右行2带动人物向右移动。

### 实现

- 原素材完整复制到 `src/DesktopPet/Assets/Video/RightWalk/`，分别命名为 `right-walk-1.mov`、`right-walk-2.mov`、`right-walk-3.mov`。
- 三段均为30 fps，分别42、215、45帧；控制器时长同步为1.4秒、约7.17秒、1.5秒，每段只播放一次。
- 视频目录解析增加 `Assets/Video/RightWalk/`，三段映射到 `TurningRight`、`WalkingRight`、`StandingRight`。
- 项目显式复制右行素材到构建和发布目录，并用 `ExcludeFromSingleFile` 保持视频为独立文件，供 ffmpeg 按路径读取。

### 验证

- 新增三段阶段边界测试；`dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj --no-restore`：94个测试通过。
- `publish.ps1` 成功更新 `publish/win-x64-current/`；三段发布视频与桌面原素材的 SHA-256 完全一致。
- 已检查三段的首帧、中间帧、末帧，人物完整，右行方向及末段转回正面的动作正确。
- 已启动更新后的发布程序，进程路径为 `publish/win-x64-current/DesktopPet.exe`，进程正常响应。

## 2026-10-02：减少动画切换卡顿

### 问题与根因

- 每次状态切换都会重新启动 ffmpeg；普通视频逻辑原先要等整段视频全部解码完，才把新片段交给渲染器。
- 因此上一段动画结束后，下一段首帧不能及时显示，表现为短暂停顿。

### 处理

- 普通视频加载增加首帧回调：首帧解码完成就立即切换显示，剩余帧继续在后台解码。
- 首帧切换时重置该状态的播放时钟，完整帧序列解码完成后只补齐缓存，不再重新等待或重置播放。

### 验证

- `dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj --no-restore`：91 个测试通过。
- `dotnet build src/DesktopPet/DesktopPet.csproj --no-restore`：通过，0 警告。
- `publish.ps1` 成功更新发布包；桌面快捷方式启动检查通过。

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
