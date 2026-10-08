# Chat Presentation Modes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a configurable lightweight bubble chat beside the pet while keeping the existing full chat window, with shared conversation context and configurable entry and dismissal behavior.

**Architecture:** Move chat ownership out of `ChatWindow` into one shared `ChatRuntime` owned by `PetWindow`. `ChatRuntime` owns the `ChatSession`, encrypted settings store, current settings, shared transcript, and the single active request. `ChatWindow` and the new `BubbleChatWindow` bind to that runtime and provide different views over the same conversation. `PetWindow` routes double-click and explicit menu commands to either view.

**Tech Stack:** .NET 8, WPF, existing `HttpClient`/DeepSeek client, `System.Text.Json`, DPAPI, xUnit.

**Spec:** Approved bounded design in the 2026-10-03 conversation; no separate design document is required.

## Global Constraints

- Keep the existing DeepSeek API contract, encrypted settings path, and `ChatSession` history semantics.
- Do not add NuGet dependencies.
- Preserve the current full chat window behavior, keyboard shortcuts, cancellation, draft recovery, and existing test constructors where practical.
- The bubble must show at most five transcript entries by default and must not create a second conversation context.
- Settings must remain backward-compatible: old encrypted settings files load with the new defaults.
- Do not persist conversation text to disk.
- Do not use batch file or directory deletion commands.

---

### Task 1: Add presentation and dismissal settings

**Files:**
- Modify: `src/DesktopPet.Core/ChatSettings.cs`
- Modify: `src/DesktopPet/ChatSettingsWindow.xaml`
- Modify: `src/DesktopPet/ChatSettingsWindow.xaml.cs`
- Test: `tests/DesktopPet.Core.Tests/DeepSeekChatTests.cs` or a new `tests/DesktopPet.Core.Tests/ChatSettingsTests.cs`
- Test: `tests/DesktopPet.Windows.Tests/ChatIntegrationTests.cs`

**Interfaces:**
- Produce `ChatPresentationMode` with values `Bubble` and `FullWindow`.
- Produce `BubbleDismissMode` with values `ClickOutside`, `ClickOutsideOrIdle`, and `AfterReply`.
- Extend `ChatSettings` with:

```csharp
public ChatPresentationMode DefaultPresentation { get; init; } = ChatPresentationMode.Bubble;
public int BubbleMessageCount { get; init; } = 5;
public BubbleDismissMode BubbleDismiss { get; init; } = BubbleDismissMode.ClickOutside;
```

- Add `ChatSettings.Normalize()` (or an equivalent store-level normalization) that clamps `BubbleMessageCount` to 3..5 and maps invalid enum values to the documented defaults.

- [ ] **Step 1: Write failing model and round-trip tests.** Verify default values, 3/4/5 message counts, all enum values, and loading an old serialized settings object without the new properties.
- [ ] **Step 2: Run the focused tests and confirm they fail for the missing properties/defaults.**
- [ ] **Step 3: Implement the enums, properties, and normalization without changing API-key/model/persona behavior.**
- [ ] **Step 4: Add settings controls.** Use a `ComboBox` for default entry, a `ComboBox` for 3/4/5 visible messages, and a `ComboBox` for dismissal mode. Initialize them from `ChatSettings`; validate and save all values through the existing `ChatSettingsStore`.
- [ ] **Step 5: Run core and Windows settings tests.** Confirm encrypted files still contain no plaintext API key/persona and that old files use Bubble/5/ClickOutside defaults.

### Task 2: Create the shared chat runtime

**Files:**
- Create: `src/DesktopPet/ChatRuntime.cs`
- Modify: `src/DesktopPet/ChatWindow.xaml.cs` to move `ChatEntry` to the shared runtime area or a small `src/DesktopPet/ChatEntry.cs`
- Modify: `src/DesktopPet/DesktopPet.csproj` only if the new files need explicit inclusion
- Test: `tests/DesktopPet.Windows.Tests/ChatRuntimeTests.cs`

**Interfaces:**
- `ChatRuntime` constructor accepts `HttpClient` and `ChatSettingsStore` so tests can inject a fake handler and temporary settings path.
- Public members:

```csharp
public ChatSession Session { get; }
public ChatSettings Settings { get; }
public ObservableCollection<ChatEntry> Entries { get; }
public bool IsBusy { get; }
public event EventHandler? StateChanged;
public Task SendAsync(string text, CancellationToken cancellationToken);
public void Cancel();
public void Clear();
public void SaveSettings(ChatSettings settings);
```

- `ChatEntry` keeps the current `Speaker`, `Text`, and `Background` properties so the existing full-window template and tests remain readable. Add a `User` or `Role` property only if the bubble template needs it; do not infer ownership from color strings.
- The runtime adds a user entry before the request and an assistant entry only after a successful response. Cancellation or service errors do not commit a message to `ChatSession`, preserve the draft in the active view, and notify both views through `StateChanged`.
- Only one request may be active. `Cancel()` is idempotent.

- [ ] **Step 1: Write tests for shared entries, successful send, cancellation, failure rollback, clear, and settings updates.** Use the existing fake `HttpMessageHandler` pattern.
- [ ] **Step 2: Run the focused tests and confirm the runtime does not exist yet.**
- [ ] **Step 3: Implement the runtime around one `ChatSession` and one `HttpClient`.** Load and normalize settings in the constructor; add the existing greeting once; keep no conversation data on disk.
- [ ] **Step 4: Run all Core and Windows chat tests.** Verify a successful message is visible through the shared collection and is present in the single `ChatSession` context.

### Task 3: Adapt the full chat window to the runtime

**Files:**
- Modify: `src/DesktopPet/ChatWindow.xaml.cs`
- Modify: `src/DesktopPet/ChatWindow.xaml` only for shared busy-state bindings or a small layout correction
- Modify: `tests/DesktopPet.Windows.Tests/ChatIntegrationTests.cs`

**Interfaces:**
- Add an application constructor `ChatWindow(ChatRuntime runtime)`.
- Retain an isolated test constructor `ChatWindow(HttpClient http, ChatSettingsStore settingsStore)` that creates a private runtime, or update tests to construct an injected runtime explicitly while preserving equivalent coverage.
- The window subscribes to `ChatRuntime.StateChanged`, uses `runtime.Entries` as its `ItemsSource`, and delegates send/cancel/clear/settings to the runtime.

- [ ] **Step 1: Update the existing integration tests to inject a runtime and add a test that a message sent from the runtime appears when the full window is shown later.**
- [ ] **Step 2: Run the existing Windows chat tests and confirm the new constructor/state wiring fails before implementation.**
- [ ] **Step 3: Remove the window-owned `ChatSession`, settings cache, and request ownership.** Keep the current status text, draft restoration, Enter/Shift+Enter behavior, and close-for-exit semantics.
- [ ] **Step 4: Verify New Chat calls `runtime.Clear()` and that settings saved by the dialog update the runtime used by both windows.**
- [ ] **Step 5: Run the full Windows test project.**

### Task 4: Build the lightweight bubble window

**Files:**
- Create: `src/DesktopPet/BubbleChatWindow.xaml`
- Create: `src/DesktopPet/BubbleChatWindow.xaml.cs`
- Create or modify: `tests/DesktopPet.Windows.Tests/BubbleChatWindowTests.cs`

**Interfaces:**
- Add `BubbleChatWindow(ChatRuntime runtime)`.
- Expose no new network API; all sending uses `runtime.SendAsync`.
- The window has named controls for tests: `MessagesList`, `InputBox`, `SendButton`, `ExpandButton`, `NewChatButton`, `StatusText`.

**Behavior:**

- Borderless, compact, non-resizable WPF window with `ShowInTaskbar=false`, owned by `PetWindow`, positioned beside the pet and clamped to the current monitor working area.
- Bind to a filtered view of `runtime.Entries` containing the last `runtime.Settings.BubbleMessageCount` entries. Refresh the filter on `StateChanged`; do not copy or fork the conversation.
- `ExpandButton` hides the bubble and asks `PetWindow` to show the full window through an event such as `ExpandRequested`.
- `NewChatButton` calls `runtime.Clear()`.
- `SendButton` and Enter use the same cancellation and draft-recovery semantics as the full window.
- `Deactivated` implements click-outside dismissal for `ClickOutside`, and is ignored while the input is focused or a request is active.
- `ClickOutsideOrIdle` additionally starts/resets a 15-second `DispatcherTimer` after successful replies and user activity; the timer must not close while typing or while a request is active.
- `AfterReply` starts a 20-second timer only after a successful reply. Focusing the input or typing cancels/restarts that timer.
- Closing from the app exit path must cancel an active request and not leave a second runtime behind.

- [ ] **Step 1: Write tests for last-N filtering, expand event, New Chat, keyboard send, cancel/draft restore, and each dismissal mode.** Drive timers with a testable clock or expose an internal timer factory; do not use long real sleeps.
- [ ] **Step 2: Run the focused tests and confirm the new window/controls are missing.**
- [ ] **Step 3: Implement the compact XAML and runtime-backed code-behind.** Keep copy concise and use the existing color/font language.
- [ ] **Step 4: Verify the bubble shows exactly 3/4/5 entries as configured and never alters the full transcript independently.**
- [ ] **Step 5: Run the focused bubble tests and then the whole Windows test project.**

### Task 5: Route pet, context-menu, and tray entry points

**Files:**
- Modify: `src/DesktopPet/PetWindow.xaml.cs`
- Modify: `src/DesktopPet/TrayController.cs`
- Modify: `tests/DesktopPet.Windows.Tests/ChatIntegrationTests.cs` or a new `ChatEntryPointTests.cs`

**Interfaces:**
- `PetWindow` owns one `ChatRuntime`, one optional `ChatWindow`, and one optional `BubbleChatWindow`.
- Add explicit methods `OpenBubbleChat()`, `OpenFullChat()`, and `OpenDefaultChat()`.
- The tray controller exposes separate `BubbleChatRequested`, `FullChatRequested`, and keeps a default-chat command only if the menu needs it.

**Behavior:**

- Double-click calls `OpenDefaultChat()` and uses `ChatSettings.DefaultPresentation`.
- The pet context menu always exposes “轻量气泡聊天” and “完整聊天窗口”; the existing single chat item is replaced or renamed so the two choices are unambiguous.
- The tray menu exposes the same two explicit choices.
- Opening one view hides the other, reuses existing windows where possible, activates the selected view, and keeps the shared conversation intact.
- Both views pause pet movement while visible or while a request is active; when the last chat view is hidden, restore the existing walk scheduling behavior.
- Bubble positioning uses the existing monitor working-area helpers and clamps the window without covering the pet when there is room.
- App exit calls `CloseForExit` on both windows, then disposes the runtime-owned `HttpClient` exactly once.

- [ ] **Step 1: Add entry-point tests for default Bubble/FullWindow routing and explicit menu commands.**
- [ ] **Step 2: Run the focused tests and confirm the new events/methods are absent.**
- [ ] **Step 3: Instantiate `ChatRuntime` in `PetWindow`, wire both windows, and update the tray/context menus.**
- [ ] **Step 4: Test switching bubble -> full window after sending a message and full window -> bubble after sending another; both must show one continuous context.**
- [ ] **Step 5: Run the full solution test suite and perform a manual WPF smoke test on a single monitor.**

### Task 6: Update documentation and verify the release path

**Files:**
- Modify: `README.md`
- Modify: `HANDOFF.md`
- Test/build: solution-wide verification commands

- [ ] **Step 1: Document the two presentation modes, default-entry setting, 3..5 bubble message count, the three dismissal choices, and the fact that conversation text remains memory-only.**
- [ ] **Step 2: Run `dotnet test DesktopPet.sln --no-restore`.**
- [ ] **Step 3: Run `dotnet build DesktopPet.sln --no-restore -c Release`.**
- [ ] **Step 4: Manually verify: first-run settings defaults, double-click routing, explicit context/tray entries, outside-click dismissal, each configured dismissal mode, expansion, New Chat, cancellation, and app exit while a request is pending.**
- [ ] **Step 5: Review the diff for unrelated changes, stale constructor calls, accidental plaintext settings, and any timer that can close while the user is typing.**

## Acceptance Criteria

- The user can choose Bubble or Full Window as the double-click destination.
- The user can choose 3, 4, or 5 visible bubble entries.
- The user can choose Click Outside, Click Outside or Idle 15s, or After Reply 20s dismissal.
- Right-click pet and tray menus always expose both explicit chat destinations.
- Bubble and full window share one `ChatSession`; switching views never loses successful messages or sends a second independent context.
- Existing DeepSeek settings encryption, cancellation, draft recovery, and full-window tests continue to pass.
- No conversation text is written to disk.
