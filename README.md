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

## Windows release editions

[Microsoft offline/cloud release](https://github.com/ltanedo/murmur/releases/tag/windows-microsoft-v1.0.0.1) ·
[Bundled GGML release](https://github.com/ltanedo/murmur/releases/tag/windows-ggml-v1.0.0.3) ·
[Mac 0.1.0](https://github.com/ltanedo/murmur/releases/tag/v0.1.0)

This branch defaults to **Microsoft online dictation**. Audio may be sent to Microsoft. Uncheck
**Use Online Dictation (restart required)** in the tray menu and restart to request Windows AI local
speech. Local speech needs Windows 11 24H2/build 26100 or later and an optional model downloaded through
Windows Update. If Windows AI is unsupported, the app falls back to online speech and notifies you.
Online recognition requires the Windows speech privacy setting and a supported speech language pack;
organization policy may prohibit it. This edition contains no Whisper model.

The signed release includes the app and its Microsoft Windows App Runtime dependency. Download both
MSIX assets to the same folder and install with:

```powershell
Add-AppxPackage -Path .\Murmur-microsoft-signed.msix -DependencyPath .\Microsoft.WindowsAppRuntime.2-experimentalF.msix
```

Both Windows editions have one package identity; installing one replaces the other. Switching to a lower
version requires uninstalling the other edition first. These PRs are alternative Windows implementations,
not two changes to merge unchanged. The Mac application is unchanged.

For a publicly trusted build, after Azure CLI login and preparing signing metadata, use:

```powershell
.\Windows\release.ps1 -Version 1.0.0.1 -OutputName Murmur-microsoft-signed -SigningMetadataPath C:\path\metadata.json
```

The release script signs the application and MSIX, timestamps both, and verifies their signatures.
The .NET build and signing are tested. End-to-end microphone recognition is unverified on this laptop:
its Windows build is below the local AI minimum, and its policy disables online recognition.

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
