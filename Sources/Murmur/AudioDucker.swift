import AudioToolbox
import CoreAudio
import Foundation

/// Turns down audio from other apps while dictating so it doesn't bleed into the mic.
///
/// Uses a Core Audio process tap: apps that are currently playing are muted at the
/// source, and their audio is played back through the output device at a lower gain.
/// This works on any output (including HDMI/DisplayPort monitors that have no
/// software volume), and only touches apps that are actually making sound.
final class AudioDucker {
    /// Fraction of the original loudness kept while ducked. 0 = mute.
    var duckFactor: Float = 0.1

    private var tapID = AudioObjectID(kAudioObjectUnknown)
    private var aggregateID = AudioObjectID(kAudioObjectUnknown)
    private var ioProcID: AudioDeviceIOProcID?
    private let gain = Gain()

    private(set) var lastError: String?

    func duck() {
        guard tapID == kAudioObjectUnknown else { return }
        lastError = nil
        let playing = Self.processesPlayingAudio()
        guard !playing.isEmpty else { return }

        let description = CATapDescription(stereoMixdownOfProcesses: playing)
        description.muteBehavior = .muted
        description.isPrivate = true
        description.name = "Murmur ducking"
        guard check(AudioHardwareCreateProcessTap(description, &tapID), "create audio tap") else {
            tapID = AudioObjectID(kAudioObjectUnknown)
            return
        }
        // Muting alone is enough when we don't want to hear anything.
        guard duckFactor > 0 else { return }

        do {
            try startPassthrough(tapUUID: description.uuid.uuidString)
        } catch {
            // Fall back to a plain mute rather than leaving the music loud.
            lastError = "Couldn't lower audio, muting instead (\(error.localizedDescription))"
            teardownPassthrough()
        }
    }

    func restore() {
        teardownPassthrough()
        if tapID != kAudioObjectUnknown {
            AudioHardwareDestroyProcessTap(tapID)
            tapID = AudioObjectID(kAudioObjectUnknown)
        }
    }

    // MARK: - Passthrough at reduced gain

    private func startPassthrough(tapUUID: String) throws {
        guard let outputUID = Self.defaultOutputDeviceUID() else {
            throw DuckerError("no output device")
        }
        let config: [String: Any] = [
            kAudioAggregateDeviceNameKey: "Murmur Ducking",
            kAudioAggregateDeviceUIDKey: "app.murmur.ducking.\(UUID().uuidString)",
            kAudioAggregateDeviceMainSubDeviceKey: outputUID,
            kAudioAggregateDeviceIsPrivateKey: true,
            kAudioAggregateDeviceIsStackedKey: false,
            kAudioAggregateDeviceTapAutoStartKey: true,
            kAudioAggregateDeviceSubDeviceListKey: [[kAudioSubDeviceUIDKey: outputUID]],
            kAudioAggregateDeviceTapListKey: [[
                kAudioSubTapDriftCompensationKey: true,
                kAudioSubTapUIDKey: tapUUID,
            ]],
        ]
        var status = AudioHardwareCreateAggregateDevice(config as CFDictionary, &aggregateID)
        guard status == noErr else { throw DuckerError("aggregate device", status) }

        // Fade from full volume down to the duck level over ~150 ms.
        gain.current = 1
        gain.target = duckFactor
        let gain = self.gain
        status = AudioDeviceCreateIOProcIDWithBlock(&ioProcID, aggregateID, nil) { _, input, _, output, _ in
            let inputs = UnsafeMutableAudioBufferListPointer(UnsafeMutablePointer(mutating: input))
            let outputs = UnsafeMutableAudioBufferListPointer(output)
            let start = gain.current
            let end = gain.step()
            for (index, out) in outputs.enumerated() {
                guard let outData = out.mData else { continue }
                let outSamples = Int(out.mDataByteSize) / MemoryLayout<Float>.size
                let dst = outData.assumingMemoryBound(to: Float.self)
                guard index < inputs.count, let inData = inputs[index].mData else {
                    dst.update(repeating: 0, count: outSamples)
                    continue
                }
                let src = inData.assumingMemoryBound(to: Float.self)
                let count = min(outSamples, Int(inputs[index].mDataByteSize) / MemoryLayout<Float>.size)
                for i in 0..<count {
                    let t = Float(i) / Float(max(count, 1))
                    dst[i] = src[i] * (start + (end - start) * t)
                }
                if count < outSamples { (dst + count).update(repeating: 0, count: outSamples - count) }
            }
        }
        guard status == noErr else { throw DuckerError("IO proc", status) }
        status = AudioDeviceStart(aggregateID, ioProcID)
        guard status == noErr else { throw DuckerError("start device", status) }
    }

    private func teardownPassthrough() {
        guard aggregateID != kAudioObjectUnknown else { return }
        if let ioProcID {
            AudioDeviceStop(aggregateID, ioProcID)
            AudioDeviceDestroyIOProcID(aggregateID, ioProcID)
        }
        ioProcID = nil
        AudioHardwareDestroyAggregateDevice(aggregateID)
        aggregateID = AudioObjectID(kAudioObjectUnknown)
    }

    private func check(_ status: OSStatus, _ what: String) -> Bool {
        if status != noErr { lastError = "Couldn't \(what) (error \(status))" }
        return status == noErr
    }

    // MARK: - CoreAudio queries

    /// Audio process objects (other than Murmur) that are currently producing sound.
    static func processesPlayingAudio() -> [AudioObjectID] {
        let ownPID = ProcessInfo.processInfo.processIdentifier
        return objectList(AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyProcessObjectList)
            .filter { process in
                let pid: pid_t? = property(process, kAudioProcessPropertyPID)
                let running: UInt32? = property(process, kAudioProcessPropertyIsRunningOutput)
                return pid != ownPID && running == 1
            }
    }

    private static func defaultOutputDeviceUID() -> String? {
        guard let device: AudioDeviceID = property(
            AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice),
            device != kAudioObjectUnknown,
            let uid: CFString = property(device, kAudioDevicePropertyDeviceUID) else { return nil }
        return uid as String
    }

    private static func property<T>(_ object: AudioObjectID, _ selector: AudioObjectPropertySelector) -> T? {
        var address = AudioObjectPropertyAddress(
            mSelector: selector, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        var size = UInt32(MemoryLayout<T>.size)
        let pointer = UnsafeMutableRawPointer.allocate(byteCount: Int(size), alignment: MemoryLayout<T>.alignment)
        defer { pointer.deallocate() }
        guard AudioObjectGetPropertyData(object, &address, 0, nil, &size, pointer) == noErr else { return nil }
        return pointer.load(as: T.self)
    }

    private static func objectList(_ object: AudioObjectID, _ selector: AudioObjectPropertySelector) -> [AudioObjectID] {
        var address = AudioObjectPropertyAddress(
            mSelector: selector, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        var size = UInt32(0)
        guard AudioObjectGetPropertyDataSize(object, &address, 0, nil, &size) == noErr, size > 0 else { return [] }
        var ids = [AudioObjectID](repeating: 0, count: Int(size) / MemoryLayout<AudioObjectID>.size)
        guard AudioObjectGetPropertyData(object, &address, 0, nil, &size, &ids) == noErr else { return [] }
        return ids
    }
}

/// Gain shared with the real-time audio thread; eases toward `target` each callback.
private final class Gain: @unchecked Sendable {
    var current: Float = 1
    var target: Float = 1

    /// Advances one audio callback (~10 ms) and returns the new gain.
    func step() -> Float {
        current += (target - current) * 0.1
        if abs(target - current) < 0.001 { current = target }
        return current
    }
}

private struct DuckerError: LocalizedError {
    let errorDescription: String?
    init(_ what: String, _ status: OSStatus? = nil) {
        errorDescription = status.map { "\(what) failed (\($0))" } ?? what
    }
}
