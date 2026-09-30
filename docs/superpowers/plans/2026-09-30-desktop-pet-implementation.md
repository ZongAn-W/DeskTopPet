# Windows Desktop Pet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a self-contained .NET 8 Windows x64 WPF desktop pet with placeholder art, movement, sleep/wake, drag-to-monitor behavior, tray controls, persisted preferences, and automated core-logic coverage.

**Architecture:** Keep platform-independent behavior in a small `DesktopPet.Core` class library so state transitions, bounds, and preference serialization are testable without a desktop. The WPF executable owns the transparent always-on-top window, timer, pointer gestures, tray icon, monitor integration, and vector placeholder renderer. Art replacement uses embedded PNG frames in `Assets/Character` with fixed canvas dimensions.

**Tech Stack:** .NET 8, C# nullable enabled, WPF, Windows Forms `NotifyIcon`/`Screen`, xUnit tests, self-contained `win-x64` publish.

---

### Task 1: Create solution and testable core contracts

**Files:**
- Create: `outputs/DesktopPet/DesktopPet.sln`
- Create: `outputs/DesktopPet/src/DesktopPet.Core/DesktopPet.Core.csproj`
- Create: `outputs/DesktopPet/src/DesktopPet.Core/PetState.cs`
- Create: `outputs/DesktopPet/src/DesktopPet.Core/PetGeometry.cs`
- Create: `outputs/DesktopPet/src/DesktopPet.Core/PetController.cs`
- Create: `outputs/DesktopPet/tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj`
- Create: `outputs/DesktopPet/tests/DesktopPet.Core.Tests/PetControllerTests.cs`

- [x] Write tests for click response, five-minute inactivity sleep, manual sleep toggle, pause preserving input, and movement clamping.
- [x] Run `dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj`; confirm the new tests fail because the core types do not exist.
- [x] Implement the minimal enums, records, controller clock/tick methods, and geometry helpers required by the tests.
- [x] Run the same test command and confirm all tests pass.

### Task 2: Add resilient preferences and monitor services

**Files:**
- Create: `outputs/DesktopPet/src/DesktopPet.Core/PetPreferences.cs`
- Create: `outputs/DesktopPet/src/DesktopPet.Core/PreferencesStore.cs`
- Create: `outputs/DesktopPet/src/DesktopPet.Core/ScreenPlacement.cs`
- Create: `outputs/DesktopPet/tests/DesktopPet.Core.Tests/PreferencesStoreTests.cs`

- [x] Add tests for round-trip JSON, corrupt JSON fallback, and missing-monitor fallback to the primary monitor.
- [x] Run the focused tests and observe the expected failure before implementation.
- [x] Implement atomic best-effort JSON persistence under `%LocalAppData%\\DesktopPet\\preferences.json`, with safe defaults and no photo access.
- [x] Implement monitor placement math using working-area rectangles, fixed pet size, and clamping.
- [x] Run all core tests and confirm they pass.

### Task 3: Build the WPF host and vector placeholder pet

**Files:**
- Create: `outputs/DesktopPet/src/DesktopPet/DesktopPet.csproj`
- Create: `outputs/DesktopPet/src/DesktopPet/App.xaml`
- Create: `outputs/DesktopPet/src/DesktopPet/App.xaml.cs`
- Create: `outputs/DesktopPet/src/DesktopPet/PetWindow.xaml`
- Create: `outputs/DesktopPet/src/DesktopPet/PetWindow.xaml.cs`
- Create: `outputs/DesktopPet/src/DesktopPet/PetVisual.cs`
- Create: `outputs/DesktopPet/src/DesktopPet/TrayController.cs`
- Create: `outputs/DesktopPet/src/DesktopPet/Assets/README.md`
- Modify: `outputs/DesktopPet/DesktopPet.sln`

- [x] Add a transparent, borderless, click-through-background WPF window with a fixed 180x210 DIP canvas and no taskbar button.
- [x] Implement vector placeholder rendering with black long hair, soft face, pale knit outfit, colorful accents, idle blink/breathing, walk direction, click wave, and sleep pose.
- [x] Wire a dispatcher timer to core ticks, screen placement, pointer click/drag threshold, right-click context menu, and monitor changes.
- [x] Implement a Windows Forms tray icon with pause/resume, sleep/wake, and exit commands; keep right-click exit available if tray initialization fails.
- [x] Confirm transparency, animation, pointer hit area, and menu behavior visually on the desktop. Build and responsive visible top-level window are confirmed. Transparency and idle animation were later verified from screenshots of the running published build; the context and tray menus were verified by code inspection only.
- [ ] Verify the right-click context menu and tray menu by hand.

### Task 4: Publish and acceptance verification

**Files:**
- Create: `outputs/DesktopPet/README.md`
- Create: `outputs/DesktopPet/publish.ps1`

- [x] Document placeholder-art replacement, runtime behavior, settings path, and excluded first-version features.
- [x] Publish self-contained Windows x64 single-file executable via `publish.ps1`.
- [x] Verify the publish directory contains the executable; SDK-less machine remains untested.
- [ ] Verify in-app exit, full desktop interaction, and dual-monitor dragging. Published startup, the full core test suite, transparency, auto-sleep, bounded walking, and the corrupt/missing/BOM preferences fallbacks are confirmed by running the published build.
