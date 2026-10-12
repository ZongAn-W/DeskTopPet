# 工作交接记录

这份文档用于记录每次开发后的实现结果、验证方式和后续注意事项。后续工作完成后，请在顶部追加一条记录。

## 2026-10-10：记忆管理面板

- 完整聊天窗口右上角的文档按钮打开非模态“记忆管理”窗口；每个聊天窗口复用同一个面板，并保留“打开文档”入口。
- 按 Markdown 原有主题分组，支持内容/主题搜索、逐条编辑内容和主题、添加、删除、撤销和重新读取。修改先留在草稿，保存后写入 UTF-8 文档；关闭面板及正常退出程序前处理未保存修改。支持 Ctrl+S、Ctrl+F、Esc。
- “自动记录新记忆”开关持久化到已有 DPAPI 加密设置，AI 设置保存保留该字段。暂停阻止新记录与待办整理，已有记忆仍用于聊天，恢复后重试旧待办。
- 按用户确认，手动保存丢弃此前当前/待整理对话，使旧摘要失效；保存前已发出的迟到聊天也不重新进入长期记忆。文档被后台或外部改变时拒绝覆盖并保留草稿。
- 新增不可变 `MemoryDocument`，按源文本范围编辑并保留其他主题、换行和未修改格式。面板记忆内容使用普通文字，嵌入 Markdown 标题/围栏等会显示说明并保留草稿；总长度超限有明确提示。不虚构来源和时间。
- 验证：最终 Release 通过199项 Core、71项 Windows测试，共270项，编译无警告。覆盖无损解析、CRUD、外部冲突、跨实例/正在摘要时手动保存、暂停恢复、重启设置和迟到回复。独立复核发现的重复文本选中错误、退出丢弃草稿及结构性 Markdown问题已修复；复核对所列问题给出 ship。Windows 测试程序集禁用并行，避免跨 STA 窗口的 WPF 共享缓存和焦点竞争。
- WPF 默认/最小尺寸、空记忆、无搜索结果截图位于 `artifacts/memory-panel-qa/`；只用示例记忆与模拟 HTTP，未修改正式记忆或发出真实 API 请求。
- 已生成独立自包含版本 `publish/win-x64-memory-panel/DesktopPet.exe`，配套视频与 FFmpeg 已复制；当前运行的 `publish/win-x64-current` 未替换。
- 文档和 pending 是两个文件。手动保存先持久化清空 pending，再写文档；第二步写入失败时会报告保存失败，但旧待办仍已丢弃，以免删除内容被重新引入。

## 2026-10-10：修复左右行走的位移和解码停顿

- 原来窗口每50 ms计时器跳1–3 DIP，历史实测约62 ms一次，而动画使用独立渲染时钟；延迟还会改变移动速度。改为在 `CompositionTarget.Rendering` 按实际秒数同步推进位移、控制器和视频，重复呈现时间不再推进。
- `WalkMotion` 按当前显示缩放累积物理像素步长，保留小数距离；菜单改为16、24、32、48 DIP/秒，旧的1.0、1.5、2.0、3.0设置保持兼容并近似保留手感。
- 出现动画开始时预解码Idle及六个左右散步片段，完整冻结帧在单个桌宠生命周期内复用；普通和外部动画最多共用两个解码槽。完整片段就绪前不推进其控制器或移动，解决单首帧停住后跳过若干帧的问题；只有实际显示帧改变才重绘。
- 解码失败在重新进入状态时按5秒冷却重试；退出取消排队及运行的解码，迟到的外部动画回调不能覆盖新的动画。
- 左行视频增加与右行一致的独立文件发布设置，确保单文件程序可通过FFmpeg找到素材。
- 素材诊断确认：左右中段视频分别177/215帧，含35/42对相邻重复帧，约24个独立画面/秒；本机完整解码约187–214 ms。程序修复不会生成额外动作帧，素材本身的轻微顿挫仍可能保留。诊断文件位于 `artifacts/walk-qa/`。
- 验证：Debug及Release均通过194项核心测试、68项Windows测试（共262项）；Release构建0警告、0错误。Windows测试按串行运行，避免窗口焦点与计时器测试相互干扰。新增覆盖不同刷新率/缩放/不规则间隔的距离、缓存复用、完整解码等待、迟到结果、并发限制、失败重试及取消。
- Release自包含版本在 `artifacts/walk-smooth-publish/`，已复制全部21个发布文件至 `publish/win-x64-current/`，安装EXE与构建SHA-256一致。
- 真实WPF透明窗口使用独立设置连续完成左/右/左/右四次散步：预加载后没有等待帧或状态不一致，控制器与视频时钟一致；只在中段移动，平均位移间隔约25–26 ms（此前约62 ms）。缓存任务身份不变，每次散步新增FFmpeg子进程均为0。QA无网络调用且正常关闭，结果在 `artifacts/walk-qa/wpf-playback-qa.json`。
- 安装的六段行走素材哈希与源码素材一致，正式新版进程47648正常响应，`Desktop Pet`窗口可见。

## 2026-10-08：对话驱动的人物情绪与回复风格

- 增加平静、开心、低落、生气、害羞、担心六种情绪及强度。由 DeepSeek 根据对话含义和近期上下文判断角色自身的反应，在同一次 JSON 模式请求中返回自然语言回复及情绪元数据，界面和长期记忆只使用回复正文。
- 人物设定继续作为基础；情绪分别调整轻快、低落、克制、含蓄和关心等表达方式。提示区分用户心情、直接对角色的态度和引用/虚构台词，并要求生气时仍保持尊重。
- `EmotionState` 限制新情绪强度最高40，同一情绪每轮最多变化20；空闲每2分钟减少1点，低于15归为平静。打开聊天或发送时计算恢复，新对话保留情绪。
- `ChatSession` 将成功的正文和情绪一起提交；失败、取消或被 Clear 作废的迟到回复不提交情绪。兼容端点返回纯文字时沿用此前情绪；损坏的结构化返回不会把 JSON 元数据展示为正文。
- `EmotionStore` 在成功回复后原子保存 `%LocalAppData%/DesktopPet/emotion.json`。重启保留情绪并计算离开期间的恢复；损坏文件回到平静，保存失败显示提示但不撤回已成功的回复。
- 气泡及完整聊天窗口头部显示共享情绪名称和色点，悬停显示强度。渲染检查覆盖六种情绪、300 DIP窄气泡及完整窗口默认/最小尺寸，图片位于 `artifacts/emotion-qa/`。
- 验证：Debug 与最终 Release 均通过165项核心测试、41项 Windows 测试，共206项，编译无警告。新增测试覆盖单次请求、强度累积、全部情绪、非法元数据、迟到回复、取消、空闲恢复、持久化和两个视图同步。
- 真实 DeepSeek 检查通过表扬、压力倾诉、冒犯、个人赞美、拒绝、引用台词及道歉七种情况，回复与情绪有一致的差异；引用冒犯台词保持平静，道歉使生气从60降为40。测试未更改正式情绪或记忆文档。
- Release 自包含发布已安装到 `publish/win-x64-current/DesktopPet.exe`，安装哈希与构建一致，重启后的进程37808正常响应。更新脚本的退出等待延至25秒，以容纳已有的退出记忆整理时间。

## 2026-10-08：对话结束后自动整理长期记忆

- 根据用户选择，在关闭完整聊天、气泡收起、开始新对话或退出时整理；气泡与完整窗口之间切换不触发整理。
- `ChatMemory` 单独收集本段对话的全部成功轮次，按完整轮次分块请求 DeepSeek 合并摘要。失败或取消的轮次不进入记忆，整理结果不混入聊天消息或最近20轮上下文。
- 长期摘要保存在 `%LocalAppData%/DesktopPet/memory.md`，下一次发送自动读取，重启及新对话保留；完整聊天窗口右上角增加打开文档的图标按钮，并检查默认及最小窗口布局。
- 待整理内容保存在 `memory.md.pending.json`，整理成功后移出待办。网络失败、取消、无效或截断摘要不覆盖已有文档；下次结束对话、保存设置或启动时重试。整理期间发现手动编辑会保留编辑结果，退出最多等待15秒，然后取消未完成请求并保留已落盘待办。
- 摘要为可编辑的 UTF-8 明文；待办文件暂存未整理的成功对话。文件无法写入时只在本次运行中保留尚未落盘的内容。整理会产生额外的 API 请求，README 已同步实际行为。
- 验证：最终 Release 测试145项核心测试、38项 Windows 测试全部通过，共183项，编译无警告。覆盖合并与重启读取、超过20轮的早期内容、失败重试、并发结束、手动编辑、损坏/截断数据、取消及退出。
- 已使用本机已有配置执行真实 DeepSeek 验证，在 `artifacts/memory-qa/test-memory.md` 中保存独立的测试称呼、偏好、计划；新的聊天可从该测试文档回忆称呼与饮茶偏好。测试数据没有写入正式记忆文档。
- Release 自包含发布已安装至 `publish/win-x64-current/DesktopPet.exe`，安装文件哈希与构建一致，重启后进程46992正常响应。正式 `memory.md` 已初始化；已有 AI 设置及桌宠素材继续使用。

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
