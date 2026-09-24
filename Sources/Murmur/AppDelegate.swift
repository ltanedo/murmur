import AppKit
import AVFoundation

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private enum State { case loading, idle, starting, recording, processing }

    /// Duck levels offered in the menu: fraction of the current volume kept while dictating.
    private static let duckLevels: [(title: String, factor: Float?)] = [
        ("Off", nil), ("Lower to 30%", 0.3), ("Lower to 10%", 0.1), ("Mute", 0),
    ]
    /// Holding the hotkey longer than this means push-to-talk; a shorter tap toggles.
    private static let holdThreshold: TimeInterval = 0.35
    /// How long to keep recording after the hotkey is released.
    private static let trailingAudio = Duration.milliseconds(500)

    private let defaults = UserDefaults.standard
    private let transcriber = Transcriber()
    private let ducker = AudioDucker()
    private let overlay = Overlay()
    private var hotKey: HotKey?
    private var statusItem: NSStatusItem!
    private var statusLine: NSMenuItem!

    private var state = State.loading {
        didSet {
            updateIcon()
            switch state {
            case .starting: overlay.show(.listening)
            case .idle, .loading: overlay.show(.hidden)
            case .recording, .processing: break // processing is shown once the trailing audio is in
            }
        }
    }
    private var pressedAt: Date?
    private var toggleMode = false
    private var stopRequested = false
    private var cancelRequested = false

    private var duckFactor: Float? {
        get { defaults.object(forKey: "duckFactor") == nil ? 0.1 : defaults.float(forKey: "duckFactor").nonNegative }
        set { defaults.set(newValue ?? -1, forKey: "duckFactor") }
    }
    private var cleanupEnabled: Bool {
        get { defaults.object(forKey: "cleanup") as? Bool ?? true }
        set { defaults.set(newValue, forKey: "cleanup") }
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        buildMenu()
        updateIcon()

        hotKey = HotKey()
        hotKey?.onPress = { [weak self] in self?.hotKeyPressed() }
        hotKey?.onRelease = { [weak self] in self?.hotKeyReleased() }
        hotKey?.onCancel = { [weak self] in self?.cancelDictation() }

        if CommandLine.arguments.contains("--demo-overlay") { demoOverlay() }
        transcriber.onLevel = { [weak self] level in self?.overlay.push(level: level) }
        TextInserter.ensureAccessibility(prompt: true)
        Task {
            _ = await AVCaptureDevice.requestAccess(for: .audio)
            do {
                setStatus("Downloading speech model…")
                try await transcriber.prepare()
                setStatus("Hold Right ⌥ to dictate · tap to toggle")
                state = .idle
            } catch {
                setStatus("Error: \(error.localizedDescription)")
            }
        }
    }

    /// Shows the overlay with a fake voice for a few seconds, for checking how it looks.
    private func demoOverlay() {
        overlay.show(.listening)
        var t = 0.0
        Timer.scheduledTimer(withTimeInterval: 0.02, repeats: true) { [weak self] timer in
            t += 0.02
            MainActor.assumeIsolated {
                self?.overlay.push(level: Float(abs(sin(t * 7) * sin(t * 2.3))))
                if t > 3 { self?.overlay.show(.processing) }
                if t > 5 { self?.overlay.show(.hidden); timer.invalidate() }
            }
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        ducker.restore() // never leave other apps muted
    }

    // MARK: - Hotkey handling

    private func hotKeyPressed() {
        switch state {
        case .idle:
            pressedAt = Date()
            toggleMode = false
            startDictation()
        case .starting, .recording:
            if toggleMode { stopDictation() }
        case .loading, .processing:
            NSSound.beep()
        }
    }

    private func hotKeyReleased() {
        guard state == .starting || state == .recording, !toggleMode else { return }
        if let pressedAt, Date().timeIntervalSince(pressedAt) < Self.holdThreshold {
            toggleMode = true // quick tap: keep listening until the next tap
        } else {
            stopDictation()
        }
    }

    private func startDictation() {
        state = .starting
        stopRequested = false
        cancelRequested = false
        if let factor = duckFactor {
            ducker.duckFactor = factor
            ducker.duck()
            if let problem = ducker.lastError { setStatus(problem) }
        }
        Task {
            do {
                try await transcriber.start()
                state = .recording
                if stopRequested { stopDictation() }
            } catch {
                log("start error: \(error)")
                ducker.restore()
                setStatus("Error: \(error.localizedDescription)")
                state = .idle
            }
        }
    }

    /// Right Option was used for a shortcut (e.g. ⌥E), not dictation: throw the recording away.
    private func cancelDictation() {
        guard state == .starting || state == .recording, !toggleMode else { return }
        cancelRequested = true
        stopDictation()
    }

    private func stopDictation() {
        guard state == .recording else {
            stopRequested = true // released while the mic was still starting up
            return
        }
        state = .processing
        Task {
            // Keep listening briefly so the last word isn't clipped by letting go of the key.
            if !cancelRequested { try? await Task.sleep(for: Self.trailingAudio) }
            overlay.show(cancelRequested ? .hidden : .processing)
            ducker.restore()
            do {
                var text = try await transcriber.stop()
                log("transcript: \"\(text)\" cancelled=\(cancelRequested) axTrusted=\(TextInserter.ensureAccessibility(prompt: false))")
                if cancelRequested { text = "" }
                if cleanupEnabled, !text.isEmpty { text = await Cleanup.polish(text) }
                if !text.isEmpty, !TextInserter.insert(text) {
                    setStatus("Copied to clipboard. Allow Accessibility to auto-paste")
                    if CommandLine.arguments.contains("--demo-overlay") { demoOverlay() }
        transcriber.onLevel = { [weak self] level in self?.overlay.push(level: level) }
        TextInserter.ensureAccessibility(prompt: true)
                }
            } catch {
                log("stop error: \(error)")
                setStatus("Error: \(error.localizedDescription)")
            }
            state = .idle
        }
    }

    // MARK: - Menu bar

    private func updateIcon() {
        let symbol = switch state {
        case .loading: "arrow.down.circle"
        case .idle: "mic"
        case .starting, .recording: "mic.fill"
        case .processing: "ellipsis.circle"
        }
        let image = NSImage(systemSymbolName: symbol, accessibilityDescription: "Murmur")
        image?.isTemplate = state != .recording
        statusItem?.button?.image = state == .recording
            ? image?.withSymbolConfiguration(.init(paletteColors: [.systemRed]))
            : image
    }

    private func setStatus(_ text: String) {
        statusLine.title = text
    }

    private func buildMenu() {
        let menu = NSMenu()
        statusLine = NSMenuItem(title: "Starting…", action: nil, keyEquivalent: "")
        statusLine.isEnabled = false
        menu.addItem(statusLine)
        menu.addItem(.separator())

        let duckItem = NSMenuItem(title: "Music While Dictating", action: nil, keyEquivalent: "")
        let duckMenu = NSMenu()
        for (index, level) in Self.duckLevels.enumerated() {
            let item = NSMenuItem(title: level.title, action: #selector(selectDuckLevel(_:)), keyEquivalent: "")
            item.tag = index
            item.target = self
            item.state = level.factor == duckFactor ? .on : .off
            duckMenu.addItem(item)
        }
        duckItem.submenu = duckMenu
        menu.addItem(duckItem)

        let cleanupItem = NSMenuItem(title: "Clean Up Filler Words", action: #selector(toggleCleanup(_:)), keyEquivalent: "")
        cleanupItem.target = self
        cleanupItem.state = cleanupEnabled ? .on : .off
        if !Cleanup.isAvailable {
            cleanupItem.title += " (needs Apple Intelligence)"
            cleanupItem.isEnabled = false
            cleanupItem.state = .off
        }
        menu.addItem(cleanupItem)

        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit Murmur", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        statusItem.menu = menu
    }

    @objc private func selectDuckLevel(_ sender: NSMenuItem) {
        duckFactor = Self.duckLevels[sender.tag].factor
        sender.menu?.items.forEach { $0.state = $0 == sender ? .on : .off }
    }

    @objc private func toggleCleanup(_ sender: NSMenuItem) {
        cleanupEnabled.toggle()
        sender.state = cleanupEnabled ? .on : .off
    }
}

private extension Float {
    /// Stored as -1 to mean "off".
    var nonNegative: Float? { self < 0 ? nil : self }
}
