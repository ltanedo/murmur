# SPEC: Murmur for Windows (system-tray dictation) and cross-platform releases

Repository: `ltanedo/murmur` (currently macOS-only, Swift Package, `Murmur.app`, v0.1.0).
Goal: add a Windows tray-app edition of Murmur in the same repository with feature
parity where Windows allows it, then publish GitHub Releases for both platforms
with package-manager and terminal install paths.

Read `README.md` and `Sources/Murmur/*.swift` first. The Mac app is the behavioural
reference; keep its semantics unless a Windows constraint below says otherwise.

---

## 1. Product behaviour to match (from the Mac app)

| Behaviour | Mac implementation | Windows requirement |
| --- | --- | --- |
| Push-to-talk on one key | Global NSEvent monitor on Right Option; hold = dictate, tap = hands-free toggle, key + other key = pass through and cancel | Same semantics on **Right Alt** (configurable; default Right Alt). Low-level keyboard hook (`WH_KEYBOARD_LL`). Must not swallow the key for normal shortcuts: if any other key is pressed while held, cancel dictation and let the chord through. |
| Mic starts instantly, no clipped words | AVAudioEngine tap starts on key-down, buffers until the engine is ready, keeps recording 500 ms after release | Start capture on key-down, buffer while the recognizer warms up, keep 500 ms tail after release. |
| Recognition | Apple `SpeechAnalyzer` + `DictationTranscriber`, on-device | `Windows.Media.SpeechRecognition.SpeechRecognizer` with a **dictation topic constraint** and `ContinuousRecognitionSession` (Microsoft's free cloud dictation; see §3). |
| Live indicator | Bottom-of-screen pill with level meter, pulsing dots while transcribing | Borderless, click-through, always-on-top WPF window at the bottom centre of the active monitor: level meter while listening, pulsing dots while finishing. Streams partial text into the pill as it arrives. |
| Optional clean-up of fillers | Apple on-device language model (`FoundationModels`) | No equivalent free on-device model on Windows. Ship a **rule-based** clean-up (um/uh/er, repeated words, false starts via simple regex list) behind the same menu toggle. Leave a hook for an LLM pass later; do not add a paid API. |
| Ducks other apps' audio while dictating | CoreAudio process tap + aggregate device | Windows Core Audio **session** API (`IAudioSessionManager2` / `ISimpleAudioVolume`): lower every other process's session volume to the chosen level (Off / 30 % / 10 % / Mute), restore on finish. Per-session ducking is sufficient; do not attempt loopback re-routing. |
| Types into any app | Pasteboard + synthesized Cmd-V, clipboard restored | Save clipboard (all formats you can round-trip, at minimum text), set text, `SendInput` Ctrl+V, restore clipboard after 300 ms. Fallback: `SendInput` Unicode key events when the foreground window rejects paste (console hosts). |
| Settings in menu | Music While Dictating, Clean Up Filler Words, Launch at Login | Tray context menu with the same three items plus **Hotkey** (Right Alt / Right Ctrl / Scroll Lock / F13) and **Open speech privacy settings**. Launch at Login via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Persist settings in `%APPDATA%\Murmur\settings.json`. |
| Permissions UX | Mic, Accessibility, Audio capture prompts | On first run, if **Online speech recognition** is off, show a one-time toast/dialog explaining it and offer to open `ms-settings:privacy-speech`. Mic permission: open `ms-settings:privacy-microphone` on access-denied. |
| Logs | `~/Library/Logs/Murmur.log` | `%LOCALAPPDATA%\Murmur\Logs\murmur.log`, rolling, no transcript text. |

Non-goals for v1: multiple languages beyond the user's Windows display language, custom vocabulary, Linux.

---

## 2. Repository layout (monorepo, one version number)

```
murmur/
  Sources/Murmur/...            # existing macOS Swift app (unchanged behaviour)
  Package.swift, build.sh, Info.plist, Resources/, Scripts/   # existing
  windows/
    Murmur.sln
    Murmur.Tray/                # WPF, net8.0-windows10.0.22621.0, x64 (+ arm64 if trivial)
      App.xaml(.cs)             # single-instance, tray icon (H.NotifyIcon or Hardcodet), no main window
      Hotkey/KeyboardHook.cs    # WH_KEYBOARD_LL, Right Alt semantics from §1
      Audio/Capture.cs          # WASAPI/NAudio mic capture, pre-roll buffer, 500 ms tail
      Speech/Dictation.cs       # SpeechRecognizer + ContinuousRecognitionSession wrapper (§3)
      Speech/Cleanup.cs         # rule-based filler removal
      Audio/Ducker.cs           # Core Audio session ducking
      Input/TextInserter.cs     # clipboard save/paste/restore + SendInput fallback
      UI/OverlayWindow.xaml     # level pill, partial text, pulsing dots
      Settings.cs, Log.cs
    installer/
      Murmur.iss                # Inno Setup script (per-user install, no admin)
      winget/                   # manifest files for a future winget submission
      scoop/murmur.json         # Scoop manifest pointing at the release zip
  docs/
    SPEC-windows-tray-app.md    # this file
    windows.md                  # how it works + troubleshooting (write it)
  .github/workflows/release.yml # §5
  VERSION                       # single source of truth, e.g. 0.2.0
```

`build.sh` (Mac) and `windows/build.ps1` (Windows) both read `VERSION`. The Mac
`Info.plist` `CFBundleShortVersionString` is stamped from `VERSION` at build time
(update `build.sh`); the Windows assembly/installer version likewise.

---

## 3. Windows speech engine: exact requirements

- API: `Windows.Media.SpeechRecognition.SpeechRecognizer`. Constraint:
  `SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation")`.
  Use `ContinuousRecognitionSession`; subscribe to `ResultGenerated` (final segments)
  and `recognizer.HypothesisGenerated` (partials for the overlay).
- Access from an **unpackaged** WPF app via NuGet `Microsoft.Windows.SDK.Contracts`
  (≥ 10.0.22621) and `Microsoft.Windows.CsWinRT`. **Verify on the first day** that
  recognition works unpackaged on Windows 11 and Windows 10 22H2; Microsoft's docs say
  package identity is required while field reports show it working. If it does not
  work unpackaged, switch the installer to MSIX with a self-signed/sparse package
  identity and document it; do not silently drop the feature.
- Requires the user's **Online speech recognition** privacy toggle. Detect the
  resulting failure (`SpeechRecognizer.CompileConstraintsAsync` / session start
  errors) and guide the user to `ms-settings:privacy-speech`.
- Warm-up: create and compile the recognizer at app start and after settings changes;
  only one recognizer instance alive at a time.
- Timeouts: set `Timeouts.InitialSilenceTimeout`, `EndSilenceTimeout`, and
  `ContinuousRecognitionSession.AutoStopSilenceTimeout` as long as the API allows
  (≥ 60 s). When the session completes on its own while the key is still held,
  **restart it immediately and keep the text so far** (same continuous-session
  pattern used in unified-unity's Android bridge). Join segments with single spaces.
- Hold ends: call `StopAsync()` (not `CancelAsync()`) so the final result flushes,
  wait up to 3 s for it, then insert.
- Engine feedback sounds: none expected from this API; if any appear, do not mute
  system streams.
- Language: use `SpeechRecognizer.SystemSpeechLanguage`; if the dictation topic is not
  supported for it, show a clear message naming the language.

---

## 4. Acceptance criteria (must all pass before release)

1. Fresh Windows 11 VM, no SDKs: install via each path in §6; a tray icon appears;
   holding Right Alt and speaking types the text into Notepad, Chrome's address bar,
   and Windows Terminal (fallback path), and the clipboard is restored.
2. A 60-second continuous dictation with several pauses yields one joined transcript
   with no words lost at pauses (log the segment boundaries).
3. Right Alt + E in an app that binds it performs the app's shortcut and does not type
   anything.
4. Hands-free: tap, speak two sentences with a 5 s pause, tap; text is inserted once.
5. Music in Spotify/browser drops to the chosen level while dictating and restores.
6. With Online speech recognition off, the app explains and opens the settings page
   instead of failing silently.
7. No transcript text in logs. No network calls other than Microsoft's speech service
   (verify with a packet capture or by inspection of the code; there must be no other
   endpoints).
8. Mac build still passes its existing manual checks (hold/tap/chord, ducking, paste)
   and `./build.sh --release` still produces `dist/Murmur-<version>.zip`.

---

## 5. Continuous integration and releases (`.github/workflows/release.yml`)

Trigger: push of a tag `v*` (and `workflow_dispatch` for dry runs).

Jobs:
- **mac** on `macos-26` (or latest with Xcode 26 SDK): `./build.sh --release`;
  upload `dist/Murmur-<v>.zip`. Keep ad-hoc signing (no Apple Developer account);
  document the Gatekeeper step as today.
- **windows** on `windows-latest`: `dotnet publish -c Release -r win-x64
  --self-contained false` (framework-dependent, requires .NET 8 Desktop Runtime; the
  installer bootstraps it) producing `Murmur-win-x64-<v>.zip` (portable) and
  `MurmurSetup-<v>.exe` (Inno Setup, per-user, adds Start Menu entry and optional
  Launch at Login). Add a `win-arm64` variant only if it builds without changes.
- **release**: create the GitHub Release `v<v>` with all assets, generated notes, and
  `SHA256SUMS.txt`. Mark pre-release if the tag contains `-`.
- Also update `installer/scoop/murmur.json` (URL + sha256) in a follow-up commit, and
  refresh the Homebrew cask in a `homebrew-murmur` tap repo (`ltanedo/homebrew-murmur`,
  cask `murmur`, pointing at the Mac zip). Create that tap repo if absent.

Versioning: bump `VERSION`, commit, tag `v<VERSION>`, push the tag. The first
cross-platform release is `v0.2.0`.

---

## 6. Install paths to document in README (both must be real and tested)

macOS:
- Homebrew tap: `brew tap ltanedo/murmur && brew install --cask murmur`
- Terminal, no Homebrew:
  `curl -L -o /tmp/Murmur.zip https://github.com/ltanedo/murmur/releases/latest/download/Murmur-<v>.zip && ditto -x -k /tmp/Murmur.zip /Applications && xattr -dr com.apple.quarantine /Applications/Murmur.app && open /Applications/Murmur.app`
  (publish a stable `Murmur-mac.zip` asset name too so the command needs no version).
- Manual: download, unzip, move to Applications, Open Anyway.

Windows:
- Installer: download and run `MurmurSetup-<v>.exe` (per-user, no admin).
- Scoop: `scoop bucket add murmur https://github.com/ltanedo/scoop-murmur` then
  `scoop install murmur` (create the bucket repo).
- winget: prepare the manifest under `windows/installer/winget/` and open the
  submission PR to `microsoft/winget-pkgs` once the release is public; until it is
  merged, document the Scoop and PowerShell routes only.
- PowerShell one-liner (portable):
  `irm https://github.com/ltanedo/murmur/releases/latest/download/install.ps1 | iex`
  where `install.ps1` (a release asset) downloads `Murmur-win-x64.zip`, extracts to
  `%LOCALAPPDATA%\Programs\Murmur`, creates a Start Menu shortcut, and launches it.

Each README install block must state the first-run permission/settings steps for
that platform (Mac: Gatekeeper, Microphone, Accessibility, Audio capture; Windows:
Online speech recognition, Microphone).

---

## 7. Working agreements

- Do not change the Mac app's behaviour in this work beyond reading `VERSION`.
- Keep the Windows code dependency-light: WPF, CsWinRT + SDK Contracts, one tray-icon
  library, NAudio (or raw WASAPI) for capture. No Electron, no bundled models.
- Never log or upload transcripts; never add telemetry.
- Commit in small steps with messages that say what and why; open one PR for the
  Windows app and one for the release pipeline/installers.
- Before tagging a release, run the acceptance list in §4 and paste the results
  (platform, OS build, outcome per item) into the PR description.
