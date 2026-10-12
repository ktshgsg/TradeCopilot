# TradeCopilot.UI

WPF overlay + tray shell for TradeCopilot. Talks to the service only over the named pipe
(`TradeCopilot.UI.Logic.PipeClient`); it never touches Core's capture or storage directly
(push-to-talk's `AudioCapture` adapter is the one documented exception).

## Stage 6b decisions

- **Push-to-talk = toggle, not hold.** `RegisterHotKey` fires once per keydown, so
  Ctrl+Alt+Space toggles recording (press to start, press again to stop) instead of a
  keyup-driven push-to-talk. A keyup-based version needs a WH_KEYBOARD_LL hook and is
  deferred to v1.1. While recording, the energy VAD closes an utterance after 800 ms of
  silence and the transcript is transcribed + queried automatically; stopping the mic
  before the VAD fires drops that partial utterance (Core's `AudioCapture.Stop` does not
  flush it).
- **Mic contention:** the service owns the default mic continuously. The UI's second
  `WaveInEvent` may fail to start; `AudioCapture.Start` logs and reports
  `IsRecording == false`, and the view model shows "Mic unavailable" instead of throwing.
- **Region picker:** numeric X/Y/W/H inputs are the v1 implementation; the full-screen
  drag picker is marked TODO(v1.1). Region and FPS are persisted to settings.json but the
  service still reads its own config in v1 (same for Language - it is "reserved":
  service-side detection is automatic).
- **Audit folder:** Settings/"View audit log" opens `<UI base>/data`. The service writes
  `data/audit.log` next to its own executable; packaged installs keep both side by side.
- **Hotkeys:** WPF's `System.Windows.Window` has no `WndProc` override (that is WinForms),
  so `MainWindow` intercepts `WM_HOTKEY` via `HwndSource.AddHook` - the WPF equivalent.
  Bindings: Ctrl+Space (overlay), Ctrl+Alt+Space (record toggle), Ctrl+Shift+P
  (pause/resume), Ctrl+Shift+X (panic).
- **Consent:** first run shows `ConsentWindow`; only "I agree" writes
  `%LOCALAPPDATA%\TradeCopilot\.consent-accepted`. Quit/X exits without writing it.
- **Tray icons** are generated 32x32 PNG-in-ICO assets (green = capturing, red = paused).

## Tests

Covered in `TradeCopilot.Tests/UiLogicTests.cs` (no WPF dependency):

- `MainViewModel_QuitCommand_DisposesPipeClientAndShutsDown`
- `MainViewModel_PurgeCacheAsync_SendsPurgeCacheRequest`
- `SettingsStore_SaveThenLoad_ReturnsSameValues`
- `ConsentGate_ShouldShow_FollowsFlagFilePresence`
- plus the pre-existing send/status/pipe round-trip tests

**Not covered (by design):** WPF-specific behavior - tray icon rendering, XAML layout,
global hotkey registration, window show/hide, consent dialog interaction. These require a
desktop session and are exercised by the Stage 6b manual test instead (consent -> overlay,
tray menu, Ctrl+Shift+P status flip, Ctrl+Shift+X panic, tray Quit leaves no process).