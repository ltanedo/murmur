import FoundationModels

/// Optional polish pass with Apple's on-device language model:
/// removes filler words and false starts without changing meaning.
enum Cleanup {
    static var isAvailable: Bool { SystemLanguageModel.default.isAvailable }

    static func polish(_ text: String) async -> String {
        guard isAvailable, text.split(separator: " ").count > 3 else { return text }
        let session = LanguageModelSession(instructions: """
            You clean up dictated text. Remove filler words (um, uh, like, you know), \
            stutters and false starts, and fix obvious punctuation and capitalization. \
            If the speaker corrects themselves ("at 3, no, at 4"), keep only the correction. \
            Do not add, summarize or answer anything. Keep the speaker's wording and language. \
            Reply with the cleaned text only.
            """)
        do {
            let response = try await session.respond(to: text)
            let cleaned = response.content.trimmingCharacters(in: .whitespacesAndNewlines)
            return cleaned.isEmpty ? text : cleaned
        } catch {
            return text
        }
    }
}
