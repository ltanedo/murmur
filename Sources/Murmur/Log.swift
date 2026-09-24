import Foundation

/// Debug logging to ~/Library/Logs/Murmur.log. Off by default because it records transcripts;
/// turn it on with `defaults write app.murmur.dictation debugLogging -bool true`.
func log(_ message: String) {
    guard UserDefaults.standard.bool(forKey: "debugLogging") else { return }
    let url = FileManager.default.homeDirectoryForCurrentUser.appending(path: "Library/Logs/Murmur.log")
    let line = "\(Date().formatted(.iso8601)) \(message)\n"
    if let handle = try? FileHandle(forWritingTo: url) {
        handle.seekToEndOfFile()
        handle.write(Data(line.utf8))
        try? handle.close()
    } else {
        try? line.write(to: url, atomically: true, encoding: .utf8)
    }
}
