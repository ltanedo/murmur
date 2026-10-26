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

### Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11
- The [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64). The .NET 8 SDK is only
  needed to build.
- The **speech language pack** for your Windows speech language, and the speech privacy setting turned on
  (see below)

### Speech language pack

Murmur uses `Windows.Media.SpeechRecognition.SpeechRecognizer`, the free recognizer built into Windows that
also powers Windows' own dictation. It needs the speech pack for your language:

1. Open **Settings → Time & language → Language & region**.
2. Next to your language, choose **… → Language options** and under *Speech* install **Basic speech
   recognition** (listed as *Speech recognition* on some builds). Windows downloads the language pack once.
3. Under **Settings → Time & language → Speech**, make sure *Speech language* is the language you just set up
   and that its pack is installed.
4. Open **Settings → Privacy & security → Speech** and turn on **Online speech recognition**. Windows gates the
   `SpeechRecognizer` dictation grammar behind this toggle and refuses to start listening while it is off; Murmur
   then shows *Turn on "Online speech recognition"…* in its tray menu and a balloon.
5. Under **Settings → Privacy & security → Microphone**, allow desktop apps to use the microphone.

Murmur checks at launch that your speech language is in the recognizer's dictation language list and shows
the missing-pack message in its tray menu otherwise.

> **Honest note on "on-device".** Murmur never sends audio anywhere itself, has no API key and configures no
> cloud service. The recognition happens inside Windows' own `SpeechRecognizer`. Microsoft's documentation
> describes the dictation grammar of that API as backed by its speech service when the toggle above is on, with
> the installed language pack providing the local engine. Which of the two Windows uses for a given phrase is
> decided by Windows, not by Murmur. If you need a guarantee that nothing leaves the machine, test with the
> network disconnected on your build of Windows.

### Build

Install the .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`), then from the repository root:

```powershell
.\Windows\build.ps1            # builds Windows\Murmur\bin\Release\...\Murmur.exe
.\Windows\build.ps1 -Release   # single-file dist\windows\Murmur.exe and dist\Murmur-windows-<version>.zip
```

Or directly: `dotnet build Windows\Murmur\Murmur.csproj -c Release`. Run `Murmur.exe` and a microphone icon
appears in the tray; the first time, Windows may ask to allow microphone access.

### Usage

| Action | What happens |
| --- | --- |
| Hold <kbd>Right Alt</kbd>, talk, release | Text is typed at the cursor |
| Tap <kbd>Right Alt</kbd>, talk, tap again | Hands-free dictation |
| <kbd>Right Alt</kbd> + another key | Normal shortcut (Alt+Tab, AltGr+E…); dictation is cancelled |

Right-click the tray icon for the menu:

- **Clean Up Filler Words:** removes "um", "uh", "er", "hmm" and repeated words with a small set of rules
  (Windows has no on-device language model in the box, so this is simpler than the Mac version).
- **Speech Settings… / Microphone Settings…:** jump to the relevant Windows privacy pages.
- `Murmur.exe --demo-overlay` plays the on-screen indicator; `Murmur.exe --debug` writes a log with transcripts
  to `%LOCALAPPDATA%\Murmur\Murmur.log`.

### Differences from the Mac app

- Text is typed with synthetic Unicode key events instead of pasting, so the clipboard is untouched. If the
  focused window runs as administrator and Murmur doesn't, Windows blocks the keystrokes; the text is then put
  on the clipboard and the tray menu says so.
- There is no audio ducking yet.
- The live indicator shows a dot that grows while the recognizer hears sound, rather than a level meter, because
  the Windows recognizer owns the microphone and doesn't expose the raw audio.
- Only languages with an installed speech pack work; the recognizer list is checked at launch.

### How it works (Windows)

| File | Role |
| --- | --- |
| [`HotKey.cs`](Windows/Murmur/HotKey.cs) | Low-level keyboard hook watching <kbd>Right Alt</kbd> for press, release and use in shortcuts |
| [`Transcriber.cs`](Windows/Murmur/Transcriber.cs) | Wraps `SpeechRecognizer` with a dictation topic constraint; a continuous recognition session runs while the key is held and every phrase it produces is joined into the transcript |
| [`Overlay.cs`](Windows/Murmur/Overlay.cs) | The floating, click-through, non-activating indicator |
| [`Cleanup.cs`](Windows/Murmur/Cleanup.cs) | Rule-based filler-word removal |
| [`TextInserter.cs`](Windows/Murmur/TextInserter.cs) | Types the text with `SendInput` Unicode events; falls back to the clipboard |
| [`TrayApp.cs`](Windows/Murmur/TrayApp.cs) | Tray icon, settings and the dictation state machine |

## Privacy

- On the Mac, audio and text are processed entirely on your machine and are never sent over the network.
- On Windows, Murmur hands audio only to the Windows speech recognizer and configures no cloud service; see
  [Speech language pack](#speech-language-pack) for what Windows itself may do.
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
- **Windows: the menu says "No dictation support for …".** Install the speech pack for your language and check
  *Speech language* in **Settings → Time & language → Speech**, then relaunch Murmur.
- **Windows: nothing happens when I hold Right Alt.** Another app may already be holding a keyboard hook, or
  the focused window is elevated. Try a non-elevated window such as Notepad.

## License

[MIT](LICENSE)
