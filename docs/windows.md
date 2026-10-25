# Murmur for Windows: how it works

The Windows edition lives in `windows/` (WPF, .NET 8, tray icon). It mirrors the Mac app's
behaviour; `docs/SPEC-windows-tray-app.md` is the full specification and acceptance list.

| File | Role |
| --- | --- |
| `Hotkey/KeyboardHook.cs` | Low-level keyboard hook on Right Alt (or Right Ctrl / Scroll Lock / F13): hold, tap for hands-free, chord passes through and cancels |
| `Speech/Dictation.cs` | `Windows.Media.SpeechRecognition` continuous dictation (Microsoft's free cloud engine). Sessions the engine ends on its own are resumed while the key is held and the segments joined |
| `Audio/Capture.cs` | Mic level meter for the overlay, with the 500 ms tail after release |
| `Audio/Ducker.cs` | Lowers other apps' audio sessions while dictating, fades them back |
| `Input/TextInserter.cs` | Pastes via Ctrl+V with the clipboard restored; types Unicode directly into console windows |
| `Speech/Cleanup.cs` | Rule-based filler/false-start removal (no on-device LLM on Windows) |
| `UI/OverlayWindow.xaml` | Click-through pill at the bottom of the screen: level, streamed words, pulsing dots |
| `App.xaml.cs` | Tray menu, settings, launch at login, the dictation state machine |

Settings: `%APPDATA%\Murmur\settings.json`. Logs: `%LOCALAPPDATA%\Murmur\Logs\murmur.log`
(no transcript text unless `DebugLogging` is true).

## Troubleshooting

- **"Speech recognition is unavailable"**: turn on Settings → Privacy & security → Speech →
  Online speech recognition (the tray menu opens it). Dictation needs a network connection.
- **Nothing is typed**: the target app may reject paste; the text is left on the clipboard.
  Console windows are typed into directly.
- **Right Alt is AltGr on your keyboard**: choose another hotkey in the tray menu.
