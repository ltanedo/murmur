import AppKit
import Carbon.HIToolbox

/// A hotkey on a single modifier key (Right Option by default) that reports press and release,
/// so it can be held to talk. If another key is typed while it's held, the user meant a
/// shortcut like ⌥E rather than dictation, so `onCancel` fires instead of `onRelease`.
///
/// Watching keys system-wide needs Accessibility permission, which Murmur already asks for.
final class HotKey {
    var onPress: (() -> Void)?
    var onRelease: (() -> Void)?
    var onCancel: (() -> Void)?

    private let keyCode: UInt16
    private let deviceMask: UInt
    private var isDown = false
    private var usedInShortcut = false
    private var monitors: [Any] = []

    /// `deviceMask` is the side-specific modifier bit (NX_DEVICERALTKEYMASK for Right Option),
    /// so holding Left Option doesn't confuse press/release tracking.
    init(keyCode: Int = kVK_RightOption, deviceMask: UInt = 0x40) {
        self.keyCode = UInt16(keyCode)
        self.deviceMask = deviceMask
        let handler: (NSEvent) -> Void = { [weak self] event in self?.handle(event) }
        let mask: NSEvent.EventTypeMask = [.flagsChanged, .keyDown]
        if let global = NSEvent.addGlobalMonitorForEvents(matching: mask, handler: handler) {
            monitors.append(global)
        }
        if let local = NSEvent.addLocalMonitorForEvents(matching: mask, handler: { handler($0); return $0 }) {
            monitors.append(local)
        }
    }

    deinit {
        monitors.forEach(NSEvent.removeMonitor)
    }

    private func handle(_ event: NSEvent) {
        switch event.type {
        case .keyDown where isDown:
            if !usedInShortcut {
                usedInShortcut = true
                onCancel?()
            }
        case .flagsChanged where event.keyCode == keyCode:
            let pressed = event.modifierFlags.rawValue & deviceMask != 0
            guard pressed != isDown else { return }
            isDown = pressed
            if pressed {
                usedInShortcut = false
                onPress?()
            } else if !usedInShortcut {
                onRelease?()
            }
        default:
            break
        }
    }
}
