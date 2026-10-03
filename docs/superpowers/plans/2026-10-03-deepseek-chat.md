# DeepSeek 桌宠对话实施计划

> 在当前会话按任务执行；保留现有素材和移动逻辑的修改。

**目标：** 双击桌宠或右键选择聊天，输入文字后获得 DeepSeek 回复。用户已明确选择先做文字聊天。

**架构：** Core 层提供 HTTP 对话客户端和会话上下文，WPF 层提供聊天及设置窗口。API 密钥与设置使用当前 Windows 用户的 DPAPI 加密保存。对话只在用户发送时联网。

**技术：** .NET 8、WPF、HttpClient、System.Text.Json、DPAPI、xUnit，无新增依赖。

## 1. 对话服务

- [x] 新建 `tests/DesktopPet.Core.Tests/DeepSeekChatTests.cs`，先验证正确鉴权和请求、只取最终回答、401/402/429 错误、空回答、取消、缺少密钥，以及历史保留与失败回滚。
- [x] 新建 `src/DesktopPet.Core/ChatSettings.cs`、`DeepSeekChatClient.cs`、`ChatSession.cs`。接口为 `Task<string> ReplyAsync(ChatSettings settings, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)` 和 `Task<string> SendAsync(string text, ChatSettings settings, CancellationToken cancellationToken)`。
- [x] 使用官方 `https://api.deepseek.com/chat/completions`，默认 `deepseek-flash`、`thinking.type=disabled`、`stream=false`、`max_tokens=1024`。保留最多 20 轮成功对话；失败或取消不提交历史。
- [x] 运行 `dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj`，13 个新增测试先失败，修复后全套 123 个通过。

## 2. 桌宠文字聊天

- [x] 新建 `src/DesktopPet/ChatSettingsStore.cs`，对整个设置对象进行 DPAPI 加密并原子写入 `%LocalAppData%/DesktopPet/ai-settings.bin`。
- [x] 新建 `ChatWindow.xaml(.cs)`，包含聊天记录、文字输入、发送/取消、设置、新对话。Enter 发送、Shift+Enter 换行，错误提示可见且输入可重试。
- [x] 新建 `ChatSettingsWindow.xaml(.cs)`，包含密码输入框、模型、人物性格。保存按钮校验密钥/模型/性格，取消不保存。
- [x] 修改 `PetWindow.xaml.cs` 和 `TrayController.cs`，添加聊天入口。聊天可见时停止走动；关闭聊天恢复自动散步日程；程序退出时取消请求。

## 3. 验证与安装

- [x] 构建并测试全部核心逻辑；用本机集成检查验证加密存储和聊天控件布局。全套129项测试通过。
- [x] 更新 README 和 HANDOFF，记录入口、API 配置、上下文范围及无密钥时无法实测云端回复的限制。
- [x] 发布到暂存目录，使用已有安全重启脚本更新正在运行的桌宠，检查进程与文件哈希。新进程43128正常响应，安装后的可执行文件哈希一致。
