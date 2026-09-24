import AVFoundation
import Speech

enum TranscriberError: LocalizedError {
    case unsupportedLocale

    var errorDescription: String? {
        switch self {
        case .unsupportedLocale: "Dictation isn't supported for your language."
        }
    }
}

/// Captures microphone audio and transcribes it on-device with Apple's DictationTranscriber.
///
/// To avoid clipping the first word, the mic starts the instant `start()` is called and
/// audio is buffered until the speech analyzer is ready; the analyzer for the next session
/// is also prepared in advance.
final class Transcriber {
    private let engine = AVAudioEngine()
    private var locale = Locale.current
    private var session: Session?
    private var nextSession: Task<Session, Error>?
    private let feed = AudioFeed()

    /// Microphone loudness (0...1), delivered on the main thread while listening.
    var onLevel: ((Float) -> Void)? {
        get { feed.onLevel }
        set { feed.onLevel = newValue }
    }

    /// Makes sure the speech model for the user's language is downloaded.
    func prepare() async throws {
        guard let supported = await DictationTranscriber.supportedLocale(equivalentTo: Locale.current) else {
            throw TranscriberError.unsupportedLocale
        }
        locale = supported
        let module = DictationTranscriber(locale: locale, preset: .shortDictation)
        if let request = try await AssetInventory.assetInstallationRequest(supporting: [module]) {
            try await request.downloadAndInstall()
        }
        prewarm()
    }

    func start() async throws {
        let started = Date()
        let input = engine.inputNode
        let micFormat = input.outputFormat(forBus: 0)

        // 1. Start listening right away; audio queues up in `feed` until the analyzer is ready.
        feed.reset(converterFrom: micFormat, to: Session.audioFormat)
        input.installTap(onBus: 0, bufferSize: 1024, format: micFormat) { [feed] buffer, _ in
            feed.receive(buffer)
        }
        engine.prepare()
        try engine.start()
        log("mic started after \(Int(Date().timeIntervalSince(started) * 1000)) ms, mic \(micFormat)")

        // 2. Hook up the (usually pre-warmed) analyzer and flush the queued audio into it.
        let locale = self.locale
        let pending = nextSession ?? Task { try await Session.make(locale: locale) }
        nextSession = nil
        let session: Session
        do {
            session = try await pending.value
        } catch {
            engine.inputNode.removeTap(onBus: 0)
            engine.stop()
            feed.finish()
            throw error
        }
        self.session = session
        feed.connect(session.continuation)
        log("analyzer attached after \(Int(Date().timeIntervalSince(started) * 1000)) ms")
    }

    /// Stops listening and returns the final transcript.
    func stop() async throws -> String {
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
        feed.finish()
        defer {
            session = nil
            prewarm()
        }
        guard let session else { return "" }
        return try await session.finish()
    }

    private func prewarm() {
        let locale = self.locale
        nextSession = Task { try await Session.make(locale: locale) }
    }
}

/// One dictation: an analyzer, its input stream and the task collecting its results.
private final class Session {
    /// DictationTranscriber's native format; the mic is converted to this as it's captured.
    static let audioFormat = AVAudioFormat(commonFormat: .pcmFormatInt16, sampleRate: 16_000,
                                           channels: 1, interleaved: true)!

    let analyzer: SpeechAnalyzer
    let continuation: AsyncStream<AnalyzerInput>.Continuation
    private let results: Task<String, Error>

    private init(analyzer: SpeechAnalyzer, continuation: AsyncStream<AnalyzerInput>.Continuation,
                 results: Task<String, Error>) {
        self.analyzer = analyzer
        self.continuation = continuation
        self.results = results
    }

    static func make(locale: Locale) async throws -> Session {
        let transcriber = DictationTranscriber(locale: locale, preset: .shortDictation)
        let analyzer = SpeechAnalyzer(modules: [transcriber])
        try await analyzer.prepareToAnalyze(in: audioFormat)

        let results = Task {
            // Results can arrive as revisions of earlier audio (and aren't always flagged
            // isFinal), so keep the newest result for each stretch of audio.
            var segments: [(range: CMTimeRange, text: String)] = []
            for try await result in transcriber.results {
                log("result final=\(result.isFinal): \(String(result.text.characters))")
                segments.removeAll { $0.range.intersection(result.range).duration > .zero
                    || $0.range.start == result.range.start }
                segments.append((result.range, String(result.text.characters)))
            }
            return segments
                .sorted { $0.range.start < $1.range.start }
                .map { $0.text.trimmingCharacters(in: .whitespaces) }
                .joined(separator: " ")
                .trimmingCharacters(in: .whitespacesAndNewlines)
        }

        let (stream, continuation) = AsyncStream<AnalyzerInput>.makeStream()
        try await analyzer.start(inputSequence: stream)
        return Session(analyzer: analyzer, continuation: continuation, results: results)
    }

    func finish() async throws -> String {
        continuation.finish()
        try await analyzer.finalizeAndFinishThroughEndOfInput()
        return try await results.value
    }
}

/// Converts mic buffers on the audio thread and hands them to the analyzer,
/// holding them in a queue until an analyzer is connected.
private final class AudioFeed: @unchecked Sendable {
    private let lock = NSLock()
    private var converter: AVAudioConverter?
    private var pending: [AVAudioPCMBuffer] = []
    private var continuation: AsyncStream<AnalyzerInput>.Continuation?
    private var buffers = 0
    var onLevel: ((Float) -> Void)?

    func reset(converterFrom micFormat: AVAudioFormat, to format: AVAudioFormat) {
        lock.withLock {
            converter = AVAudioConverter(from: micFormat, to: format)
            converter?.downmix = true // mix all mic channels instead of using only the first
            pending = []
            continuation = nil
            buffers = 0
        }
    }

    func receive(_ buffer: AVAudioPCMBuffer) {
        lock.withLock {
            buffers += 1
            if buffers % 100 == 1 { log("mic buffer #\(buffers), peak \(Self.peak(buffer))") }
            if let onLevel {
                let level = Self.level(buffer)
                DispatchQueue.main.async { onLevel(level) }
            }
            guard let converter, let converted = Self.convert(buffer, with: converter) else { return }
            if let continuation {
                continuation.yield(AnalyzerInput(buffer: converted))
            } else {
                pending.append(converted)
            }
        }
    }

    func connect(_ continuation: AsyncStream<AnalyzerInput>.Continuation) {
        lock.withLock {
            log("flushing \(pending.count) queued buffers")
            pending.forEach { continuation.yield(AnalyzerInput(buffer: $0)) }
            pending = []
            self.continuation = continuation
        }
    }

    /// Called after the mic has stopped, so everything captured has been delivered.
    func finish() {
        lock.withLock {
            continuation = nil
            converter = nil
        }
    }

    /// RMS loudness mapped from -50...-10 dBFS onto 0...1.
    private static func level(_ buffer: AVAudioPCMBuffer) -> Float {
        guard let data = buffer.floatChannelData?[0], buffer.frameLength > 0 else { return 0 }
        var sum: Float = 0
        for i in 0..<Int(buffer.frameLength) { sum += data[i] * data[i] }
        let db = 10 * log10(max(sum / Float(buffer.frameLength), 1e-10))
        return min(max((db + 50) / 40, 0), 1)
    }

    private static func peak(_ buffer: AVAudioPCMBuffer) -> Float {
        guard let data = buffer.floatChannelData?[0] else { return -1 }
        var peak: Float = 0
        for i in 0..<Int(buffer.frameLength) { peak = max(peak, abs(data[i])) }
        return peak
    }

    private static func convert(_ buffer: AVAudioPCMBuffer, with converter: AVAudioConverter) -> AVAudioPCMBuffer? {
        let format = converter.outputFormat
        let ratio = format.sampleRate / buffer.format.sampleRate
        let capacity = AVAudioFrameCount((Double(buffer.frameLength) * ratio).rounded(.up)) + 1
        guard let output = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: capacity) else { return nil }
        var consumed = false
        var error: NSError?
        converter.convert(to: output, error: &error) { _, status in
            if consumed {
                status.pointee = .noDataNow
                return nil
            }
            consumed = true
            status.pointee = .haveData
            return buffer
        }
        return error == nil && output.frameLength > 0 ? output : nil
    }
}
