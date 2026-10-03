# Task 2 Report: Shared Chat Runtime

## Implementation

- Added `ChatRuntime` with the required constructor and public members.
- The runtime owns exactly one `ChatSession`, one injected `HttpClient`, the normalized current `ChatSettings`, and an in-memory `ObservableCollection<ChatEntry>` transcript.
- A greeting is added once at construction and after `Clear`; it is not added to `ChatSession` context.
- `SendAsync` adds a provisional user entry, commits the user and assistant messages to the single session only when the service call succeeds, and removes the provisional entry after cancellation or service failure.
- `Cancel` is idempotent and only one request can be active. `StateChanged` is raised when request state or transcript/settings state changes.
- `SaveSettings` normalizes, persists through `ChatSettingsStore`, updates the shared settings, and raises `StateChanged`.
- Added an explicit `Role` property to `ChatEntry` while preserving the existing localized `Speaker` display label and `Background` properties. Alternate views can use `Role` instead of inferring ownership from colors.
- `ChatRuntime` disposes the injected `HttpClient` exactly once; the application can therefore dispose the runtime during its exit path.
- Conversation entries are never written to disk. Only settings pass through `ChatSettingsStore`.

## Tests

`ChatRuntimeTests` covers greeting/settings loading, successful shared transcript/session state, cancellation rollback, service-failure rollback, clear/reset, settings normalization and persistence, busy-state notifications, and rejection of a second active request.

Commands run:

- `dotnet test tests/DesktopPet.Windows.Tests/DesktopPet.Windows.Tests.csproj --no-restore --filter FullyQualifiedName~ChatRuntimeTests` - 7 passed.
- `dotnet test tests/DesktopPet.Windows.Tests/DesktopPet.Windows.Tests.csproj --no-restore` - 14 passed.
- `dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj --no-restore` - 132 passed.

## Notes

Unreadable encrypted settings currently fall back to normalized defaults in the runtime constructor, matching first-run behavior without writing over the unreadable file. The existing settings window still reports unreadable settings when it loads directly. Task 3 should decide how the owning window surfaces this runtime-level fallback if that distinction needs to be visible.
