# Desktop Pet

This is a native WPF desktop pet targeting .NET 8 and Windows x64. The first version uses a vector placeholder character and keeps all reference photographs outside the application. It supports idle breathing/blinking, walking along the active monitor's working-area bottom edge, click response, automatic sleep after five minutes without interaction, manual sleep/wake, drag-to-monitor, pause/resume, right-click commands, a notification-area menu, and persisted position/state.

The character now ships with transparent photo-derived frames under `src/DesktopPet/Assets/Character`; the source photograph is not bundled or read at runtime. See `src/DesktopPet/Assets/README.md` for the six state prefixes and naming convention. Keep every frame on the 180 x 210 canvas with a stable feet baseline when replacing the art.

Preferences are stored at `%LocalAppData%\\DesktopPet\\preferences.json`; malformed settings fall back to the primary monitor and safe defaults. Chat, networking, reminders, growth, multiple characters, and startup launch are intentionally outside this version.

Build and test:

```powershell
dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj
dotnet build src/DesktopPet/DesktopPet.csproj
```
