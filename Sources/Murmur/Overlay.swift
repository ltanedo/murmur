import AppKit
import SwiftUI

/// The floating pill at the bottom of the screen that shows Murmur is listening.
@MainActor
final class Overlay {
    enum Mode { case hidden, listening, processing }

    private let model = OverlayModel()
    private lazy var panel: NSPanel = makePanel()

    func show(_ mode: Mode) {
        model.mode = mode
        if mode == .hidden {
            NSAnimationContext.runAnimationGroup({ $0.duration = 0.2; panel.animator().alphaValue = 0 }) {
                Task { @MainActor in if self.model.mode == .hidden { self.panel.orderOut(nil) } }
            }
            return
        }
        if mode == .listening { model.levels = Array(repeating: 0, count: OverlayModel.barCount) }
        if !panel.isVisible {
            position()
            panel.alphaValue = 0
            panel.orderFrontRegardless()
        }
        NSAnimationContext.runAnimationGroup { $0.duration = 0.15; panel.animator().alphaValue = 1 }
    }

    /// Feeds the latest microphone level (0...1) to the waveform.
    func push(level: Float) {
        guard model.mode == .listening else { return }
        model.levels.removeFirst()
        model.levels.append(CGFloat(level))
    }

    private func position() {
        // Show on the screen the user is working on (the one with the mouse), above the Dock.
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { $0.frame.contains(mouse) } ?? NSScreen.main
        guard let area = screen?.visibleFrame else { return }
        let size = panel.frame.size
        panel.setFrameOrigin(NSPoint(x: area.midX - size.width / 2, y: area.minY + 16))
    }

    private func makePanel() -> NSPanel {
        let size = NSSize(width: 120, height: 36)
        let panel = NSPanel(contentRect: NSRect(origin: .zero, size: size),
                            styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.ignoresMouseEvents = true
        panel.contentView = NSHostingView(rootView: OverlayView(model: model).frame(width: size.width, height: size.height))
        return panel
    }
}

@MainActor @Observable
private final class OverlayModel {
    static let barCount = 14
    var mode = Overlay.Mode.hidden
    var levels = [CGFloat](repeating: 0, count: barCount)
}

private struct OverlayView: View {
    let model: OverlayModel

    var body: some View {
        ZStack {
            Capsule().fill(.black.opacity(0.88))
            Capsule().strokeBorder(.white.opacity(0.15), lineWidth: 1)
            switch model.mode {
            case .processing: ProcessingDots()
            default: waveform
            }
        }
    }

    private var waveform: some View {
        HStack(spacing: 3) {
            ForEach(model.levels.indices, id: \.self) { index in
                Capsule()
                    .fill(.white)
                    .frame(width: 3, height: 4 + model.levels[index] * 18)
            }
        }
        .animation(.easeOut(duration: 0.08), value: model.levels)
    }
}

private struct ProcessingDots: View {
    var body: some View {
        TimelineView(.animation) { context in
            let t = context.date.timeIntervalSinceReferenceDate
            HStack(spacing: 5) {
                ForEach(0..<3) { index in
                    Circle()
                        .fill(.white)
                        .frame(width: 5, height: 5)
                        .opacity(0.35 + 0.65 * (0.5 + 0.5 * sin(t * 6 - Double(index) * 0.8)))
                }
            }
        }
    }
}
