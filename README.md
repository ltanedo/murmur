<p align="center">
  <img src="Resources/icon.png" width="128" alt="Murmur icon">
</p>

<h1 align="center">Murmur</h1>

<p align="center">
  Private, on-device voice dictation for the Mac menu bar.<br>
  Hold <kbd>Right ⌥</kbd>, talk, let go, and your words are typed wherever your cursor is.
</p>

---

Murmur is a small, native dictation app in the spirit of Aqua Voice, Wispr Flow and Google AI Edge Eloquent.
Everything runs locally on your Mac with Apple's built-in speech and language models, so no audio or text
ever leaves the machine and there is nothing to sign up for.

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

- macOS 26 (Tahoe) or later on Apple silicon, or Windows 10 22H2 / Windows 11 (see Install)
- Apple Intelligence turned on, for the optional clean-up on the Mac only

## Install

### macOS

Homebrew:

```bash
brew tap ltanedo/murmur
brew install --cask murmur
```

Terminal, without Homebrew:

```bash
curl -L -o /tmp/Murmur.zip https://github.com/ltanedo/murmur/releases/latest/download/Murmur-mac.zip \
  && ditto -x -k /tmp/Murmur.zip /Applications \
  && xattr -dr com.apple.quarantine /Applications/Murmur.app \
  && open /Applications/Murmur.app
```

Manual: download `Murmur-<version>.zip` from the [latest release](../../releases/latest), unzip it and
move `Murmur.app` to `/Applications`. Murmur isn't notarized, so the first launch needs
**System Settings → Privacy & Security → Open Anyway** (the `xattr` line above does the same).

Open Murmur. A mic icon appears in the menu bar, and macOS asks for these permissions:

| Permission | Why |
| --- | --- |
| Microphone | To hear you while the hotkey is held |
| Accessibility | To detect <kbd>Right ⌥</kbd> and paste the text into other apps |
| Audio capture | To turn down other apps' audio while you dictate |

After granting Accessibility, quit Murmur and open it again. The first launch downloads Apple's
speech model for your language. The menu shows progress.

### Windows

Installer: download and run `MurmurSetup-<version>.exe` from the [latest release](../../releases/latest)
(per-user, no admin rights).

Scoop:

```powershell
scoop bucket add murmur https://github.com/ltanedo/scoop-murmur
scoop install murmur
```

PowerShell, portable:

```powershell
irm https://github.com/ltanedo/murmur/releases/latest/download/install.ps1 | iex
```

Requirements: Windows 10 22H2 or Windows 11, the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0),
and **Settings → Privacy & security → Speech → Online speech recognition** turned on (the Windows edition uses
Microsoft's built-in dictation service, which is free and needs no account). Allow microphone access when asked.
The tray icon's menu has shortcuts to both settings pages. Hold <kbd>Right Alt</kbd> to dictate; see
[docs/windows.md](docs/windows.md) for how it works.

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

## Privacy

- Audio and text are processed entirely on your Mac and are never sent over the network.
- Nothing is stored. Debug logging is off by default. You can turn it on for troubleshooting, but it records
  transcripts to `~/Library/Logs/Murmur.log`:

  ```bash
  defaults write app.murmur.dictation debugLogging -bool true
  ```

## Troubleshooting

- **The indicator shows but no text appears.** Accessibility is probably off. Your text is on the clipboard, so
  press <kbd>⌘V</kbd>. Then re-enable Murmur in **Privacy & Security → Accessibility** and relaunch.
- **Music doesn't get quieter.** Check that Murmur is allowed under **Privacy & Security → Screen & System
  Audio Recording** (shown as audio capture).
- **The first word is cut off.** Some USB mics take around 300 ms to start. Pause briefly after pressing the key.

## License

[MIT](LICENSE)
