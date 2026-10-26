<p align="center">
  <img src="Resources/icon.png" width="128" alt="Murmur icon">
</p>

<h1 align="center">Murmur</h1>

<p align="center">
  Private, on-device voice dictation for the Mac menu bar and the Windows tray.<br>
  Hold <kbd>Right ⌥</kbd> (Mac) or <kbd>Right Alt</kbd> (Windows), talk, let go, and your words are typed wherever your cursor is.
</p>

---

Murmur is a small, native dictation app in the spirit of Aqua Voice, Wispr Flow and Google AI Edge Eloquent.
Everything runs locally with the speech engine that ships with your operating system (Apple Speech on macOS,
the in-box Windows speech recognizer on Windows), so there is no account, no API key and nothing to sign up for.

The Mac app is the original; the [Windows app](#windows) is a port with the same hold-to-talk behaviour.

## Features

- **Push-to-talk on one key.** Hold <kbd>Right ⌥</kbd> to talk and release to insert the text. Tap it instead
  for hands-free mode, and tap again to stop.
- **Turns your music down while you talk.** Spotify, Music, YouTube and anything else making sound is lowered
  to 30%, lowered to 10%, or muted while you dictate, then faded back up. This works on every output, including
  HDMI and DisplayPort monitors that macOS can't change the volume of.
- **Doesn't clip your words.** The mic starts the instant you press the key and audio is buffered while the speech
  engine gets ready. Recording continues for half a second after you let go.
- **Live indicator.** A small pill at the bottom of the screen shows your voice level while you talk and pulsing
  dots while it transcribes.
- **Optional clean-up.** Removes "um", "uh", stutters and false starts with Apple's on-device language model.
- **Types into any app.** Text is pasted at the cursor and your clipboard is restored afterwards.
- **Still a normal key.** If you use <kbd>Right ⌥</kbd> in a shortcut (like <kbd>⌥E</kbd>), dictation is cancelled.

## Requirements

- macOS 26 (Tahoe) or later
- Apple silicon Mac
- Apple Intelligence turned on, for the optional clean-up only

## Install

1. Download `Murmur-x.y.z.zip` from the [latest release](../../releases/latest), unzip it and move
   `Murmur.app` to `/Applications`.
2. Murmur isn't notarized, so macOS blocks it the first time. Either open **System Settings → Privacy &
   Security** and click **Open Anyway**, or run:

   ```bash
   xattr -dr com.apple.quarantine /Applications/Murmur.app
   ```

3. Open Murmur. A mic icon appears in the menu bar, and macOS asks for these permissions:

   | Permission | Why |
   | --- | --- |
   | Microphone | To hear you while the hotkey is held |
   | Accessibility | To detect <kbd>Right ⌥</kbd> and paste the text into other apps |
   | Audio capture | To turn down other apps' audio while you dictate |

   After granting Accessibility, quit Murmur and open it again.

The first launch downloads Apple's speech model for your language. The menu shows progress.

## Usage

| Action | What happens |
| --- | --- |
| Hold <kbd>Right ⌥</kbd>, talk, release | Text is typed at the cursor |
| Tap <kbd>Right ⌥</kbd>, talk, tap again | Hands-free dictation |
| <kbd>Right ⌥</kbd> + another key | Normal shortcut; dictation is cancelled |

Settings are in the menu bar menu:

- **Music While Dictating:** Off, Lower to 30%, Lower to 10% (the default) or Mute
- **Clean Up Filler Words:** on by default when Apple Intelligence is available

## Build from source

You need the Xcode command line tools with the macOS 26 SDK.

```bash
git clone https://github.com/ltanedo/murmur.git
cd murmur
./build.sh            # builds ./Murmur.app
open Murmur.app
```

Other commands:

- `./build.sh --release` builds `dist/Murmur.app` and `dist/Murmur-<version>.zip`.
- `open Murmur.app --args --demo-overlay` plays the on-screen indicator with a fake voice.
- `swift Scripts/make-icon.swift` regenerates the app icon.

> **Note:** builds are ad-hoc signed, so macOS treats every rebuild as a new app and turns off its
> Accessibility permission. After rebuilding, remove Murmur from **Privacy & Security → Accessibility**,
> add it again and relaunch.

## How it works

| File | Role |
| --- | --- |
| [`HotKey.swift`](Sources/Murmur/HotKey.swift) | Watches <kbd>Right ⌥</kbd> for press, release and use in shortcuts |
| [`Transcriber.swift`](Sources/Murmur/Transcriber.swift) | Captures the mic with `AVAudioEngine`, converts it to 16 kHz mono and streams it to `SpeechAnalyzer` with a `DictationTranscriber`. A session is prepared ahead of time so dictation starts instantly |
| [`AudioDucker.swift`](Sources/Murmur/AudioDucker.swift) | Creates a Core Audio process tap on the apps that are playing sound, mutes them at the source and plays their audio back through a private aggregate device at a lower gain |
| [`Overlay.swift`](Sources/Murmur/Overlay.swift) | The floating, click-through indicator (SwiftUI in a non-activating panel) |
| [`Cleanup.swift`](Sources/Murmur/Cleanup.swift) | Optional filler-word removal with the `FoundationModels` on-device model |
| [`TextInserter.swift`](Sources/Murmur/TextInserter.swift) | Pastes with a synthetic <kbd>⌘V</kbd> and restores the clipboard. Without Accessibility the text is left on the clipboard instead |
| [`AppDelegate.swift`](Sources/Murmur/AppDelegate.swift) | Menu bar item, settings and the dictation state machine |

## Windows

The Windows port lives in [`Windows/`](Windows/) and is a small .NET tray app. It does the same job: hold
<kbd>Right Alt</kbd>, talk, let go, and the text is typed into whatever has focus.

> **Experimental.** The Windows build uses the Windows AI speech recognition API
> (`Microsoft.Windows.AI.Speech`), which Microsoft ships only in the *experimental* channel of the Windows App
> SDK. It is English-only in the current preview, not supported for production, and apps that use it must not be
> published to the Microsoft Store. Expect the API, and this build, to change.

### Speech engines

| Engine | When it's used | Where audio goes |
| --- | --- | --- |
| **On-device** (`Microsoft.Windows.AI.Speech`) | Whenever this PC can run it | Nowhere. The model runs on the NPU of a Copilot+ PC or on the CPU. No key, no account, no Azure. |
| **Online dictation** (`Windows.Media.SpeechRecognition.SpeechRecognizer`) | Only when the on-device model reports *not supported on this system* | Handled by Windows' own dictation, which may send audio to Microsoft's speech service. |

The tray menu always shows which one is active (*Engine: On-device* or *Engine: Online dictation*), and Murmur
shows a notification if it had to fall back.

### Requirements

- Windows 11, version 24H2 (build 26100) or later, for the on-device engine. The fallback runs on Windows 10
  2004 and later.
- A Copilot+ PC with an NPU (model preinstalled), or any x64 PC. On a CPU-only PC Microsoft recommends 4+ cores,
  a 3 GHz base clock and 32 MB of L3 cache for real-time use; the model is an optional download.
- Windows App Runtime 2.5.4-experimental (the build script installs it for you).
- .NET 8 SDK to build. Nothing else: the Windows SDK tools come from NuGet.

### Speech model and language pack

**On-device engine.** The first time Murmur starts on a CPU-only PC, it asks for permission to download the
*speech recognition model*, which Windows fetches in the background through Windows Update. You can watch
progress under **Settings → Windows Update** and remove the model later under **Settings → System → AI
components**. On Copilot+ PCs the model is already there. The preview model recognises English only.

**Online dictation fallback.** If Murmur reports *Online dictation*, Windows' dictation needs:

1. The **speech language pack** for your Windows speech language: **Settings → Time & language → Language &
   region → … → Language options → Speech → Basic speech recognition**. Check *Speech language* under
   **Settings → Time & language → Speech**.
2. **Online speech recognition** turned on under **Settings → Privacy & security → Speech**. Windows refuses to
   start the dictation grammar while it is off, and Murmur says so in its tray menu.

In both cases, allow Murmur under **Settings → Privacy & security → Microphone**.

### Build and install

Install the .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`), then from the repository root:

```powershell
.\Windows\build.ps1             # publishes the app, packs dist\windows\Murmur.msix and signs it with a dev certificate
.\Windows\build.ps1 -Install    # also installs the Windows App Runtime (experimental) and Murmur for your user
```

The app has to run as an MSIX package: the Windows AI APIs require package identity and the `systemAIModels`
capability, which [`Package.appxmanifest`](Windows/Murmur/Package.appxmanifest) declares together with
`microphone` and `runFullTrust`. The script builds the package with `makeappx` and `signtool` from the
`Microsoft.Windows.SDK.BuildTools` NuGet package, so Visual Studio is not needed. The Windows App Runtime
framework package comes from the `Microsoft.WindowsAppSDK.Runtime` NuGet package.

Installing a locally built package needs one of:

- **Developer Mode** (**Settings → System → For developers**): the script registers the loose layout
  directly, no certificate involved.
- Otherwise the script creates a self-signed *Murmur Developer* certificate in your user store, signs the
  package with it and adds it to your user's Trusted Root store. Windows shows a confirmation dialog for that
  once. Nothing is written to the machine-wide stores and no admin rights are needed.

The certificate's private key never leaves your Windows certificate store and no secrets are in the repository.

### Usage

| Action | What happens |
| --- | --- |
| Hold <kbd>Right Alt</kbd>, talk, release | Text is typed at the cursor |
| Tap <kbd>Right Alt</kbd>, talk, tap again | Hands-free dictation |
| <kbd>Right Alt</kbd> + another key | Normal shortcut (Alt+Tab, AltGr+E…); dictation is cancelled |

Right-click the tray icon for the menu:

- **Engine:** which speech engine is active (see above).
- **Clean Up Filler Words:** removes "um", "uh", "er", "hmm" and repeated words with a small set of rules
  (Windows has no on-device language model in the box, so this is simpler than the Mac version).
- **Speech Settings… / Microphone Settings…:** jump to the relevant Windows privacy pages.
- `Murmur.exe --demo-overlay` plays the on-screen indicator; `Murmur.exe --debug` writes a log with transcripts
  to `%LOCALAPPDATA%\Murmur\Murmur.log` (inside the package's virtualised AppData when installed).

### Differences from the Mac app

- Text is typed with synthetic Unicode key events instead of pasting, so the clipboard is untouched. If the
  focused window runs as administrator and Murmur doesn't, Windows blocks the keystrokes; the text is then put
  on the clipboard and the tray menu says so.
- There is no audio ducking yet.
- The live indicator shows a dot that grows while the recognizer hears speech, rather than a level meter,
  because the speech APIs own the microphone and don't expose the raw audio.
- The on-device model is English-only in the preview.

### How it works (Windows)

| File | Role |
| --- | --- |
| [`HotKey.cs`](Windows/Murmur/HotKey.cs) | Low-level keyboard hook watching <kbd>Right Alt</kbd> for press, release and use in shortcuts |
| [`ISpeechEngine.cs`](Windows/Murmur/ISpeechEngine.cs) | The engine contract: prepare, start, stop with transcript, cancel |
| [`OnDeviceSpeechEngine.cs`](Windows/Murmur/OnDeviceSpeechEngine.cs) | `SpeechRecognitionModelFactory.Default` → `GetReadyState` → `EnsureReadyAsync` (with consent) → `CreateAsync`, then a `StreamingRecognition` on the default microphone while the key is held; final phrases plus the last partial become the transcript |
| [`OnlineSpeechEngine.cs`](Windows/Murmur/OnlineSpeechEngine.cs) | Fallback on `Windows.Media.SpeechRecognition` with a dictation constraint |
| [`Overlay.cs`](Windows/Murmur/Overlay.cs) | The floating, click-through, non-activating indicator |
| [`Cleanup.cs`](Windows/Murmur/Cleanup.cs) | Rule-based filler-word removal |
| [`TextInserter.cs`](Windows/Murmur/TextInserter.cs) | Types the text with `SendInput` Unicode events; falls back to the clipboard |
| [`TrayApp.cs`](Windows/Murmur/TrayApp.cs) | Tray icon, engine selection, settings and the dictation state machine |
| [`Package.appxmanifest`](Windows/Murmur/Package.appxmanifest) / [`build.ps1`](Windows/build.ps1) | MSIX identity, capabilities and the Windows App Runtime dependency; publish, pack, sign, install |

## Privacy

- On the Mac, audio and text are processed entirely on your machine and are never sent over the network.
- On Windows with the on-device engine, audio is processed by the Windows AI speech model on your PC and never
  sent anywhere. Only the *Online dictation* fallback may send audio to Microsoft; see
  [Speech engines](#speech-engines).
- Nothing is stored. Debug logging is off by default. You can turn it on for troubleshooting, but it records
  transcripts to `~/Library/Logs/Murmur.log`:

  ```bash
  defaults write app.murmur.dictation debugLogging -bool true
  ```

  On Windows, run `Murmur.exe --debug` to log to `%LOCALAPPDATA%\Murmur\Murmur.log`.

## Troubleshooting

- **The indicator shows but no text appears.** Accessibility is probably off. Your text is on the clipboard, so
  press <kbd>⌘V</kbd>. Then re-enable Murmur in **Privacy & Security → Accessibility** and relaunch.
- **Music doesn't get quieter.** Check that Murmur is allowed under **Privacy & Security → Screen & System
  Audio Recording** (shown as audio capture).
- **The first word is cut off.** Some USB mics take around 300 ms to start. Pause briefly after pressing the key.
- **Windows: the menu says "Engine: Online dictation".** This PC can't run the on-device model (it needs
  Windows 11 24H2 and a supported NPU or CPU). The fallback needs the speech language pack and the *Online
  speech recognition* toggle; see [Speech model and language pack](#speech-model-and-language-pack).
- **Windows: "Windows AI speech isn't available here (Class not registered)".** Murmur was started from the
  build folder instead of as the installed package. Run `.\Windows\build.ps1 -Install` and start it from the
  Start menu.
- **Windows: nothing happens when I hold Right Alt.** Another app may already be holding a keyboard hook, or
  the focused window is elevated. Try a non-elevated window such as Notepad.

## License

[MIT](LICENSE)
