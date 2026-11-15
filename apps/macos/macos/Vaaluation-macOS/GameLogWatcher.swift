import Foundation

/// Tails the Path of Exile client log and reports newly appended lines.
///
/// Privacy: this class only reads and forwards raw lines; deciding what is a
/// trade whisper (and discarding everything else) happens in TypeScript. It
/// starts at the end of the file, so existing chat history is never read.
final class GameLogWatcher {
  static let shared = GameLogWatcher()

  /// Locations the macOS client writes its log to, most likely first.
  private static let candidatePaths: [String] = [
    "~/Library/Application Support/Path of Exile/Cache/Logs/Client.txt",
    "~/Library/Application Support/Path of Exile/logs/Client.txt",
    "~/Library/Application Support/Steam/steamapps/common/Path of Exile/Cache/Logs/Client.txt",
    "~/Library/Application Support/Steam/steamapps/common/Path of Exile/logs/Client.txt",
  ]

  private var handle: FileHandle?
  private var source: DispatchSourceFileSystemObject?
  private var offset: UInt64 = 0
  private var partial = ""
  private let queue = DispatchQueue(label: "com.vaaluation.gamelog")

  /// Called on the main queue with each newly appended line.
  var onLines: (([String]) -> Void)?

  private init() {}

  /// Cheap shape test used only to decide whether to surface the overlay.
  /// Authoritative parsing stays in TypeScript; this must not be relied on
  /// for anything the user sees.
  static func looksLikeTradeWhisper(_ line: String) -> Bool {
    guard line.contains("@From") else { return false }
    return line.range(of: #"(would|want to) like to buy|'?d like to buy|like to buy your"#,
                      options: .regularExpression) != nil
  }

  static func resolveLogPath() -> String? {
    // Testing seam: point the watcher at a scratch file so the whole path
    // can be exercised without writing to the game's own log.
    if let override = ProcessInfo.processInfo.environment["VAALUATION_LOG_PATH"],
      FileManager.default.isReadableFile(atPath: override)
    {
      return override
    }
    if let folder = GameLocator.runningGame()?.bundleURL?.deletingLastPathComponent() {
      for relativePath in ["Cache/Logs/Client.txt", "logs/Client.txt"] {
        let path = folder.appendingPathComponent(relativePath).path
        if FileManager.default.isReadableFile(atPath: path) {
          return path
        }
      }
    }
    return candidatePaths
      .map { ($0 as NSString).expandingTildeInPath }
      .compactMap { path -> (path: String, modified: Date)? in
        guard FileManager.default.isReadableFile(atPath: path),
          let attributes = try? FileManager.default.attributesOfItem(atPath: path),
          let modified = attributes[.modificationDate] as? Date
        else { return nil }
        return (path, modified)
      }
      .max { $0.modified < $1.modified }?.path
  }

  private(set) var currentPath: String?
  var isWatching: Bool { source != nil }

  @discardableResult
  func start() -> Bool {
    guard let path = Self.resolveLogPath() else {
      LogStore.shared.append(
        level: "warn",
        scope: "gamelog",
        message: "Could not find the Path of Exile client log."
      )
      return false
    }

    if source != nil && currentPath == path { return true }
    guard let handle = FileHandle(forReadingAtPath: path) else {
      LogStore.shared.append(
        level: "warn",
        scope: "gamelog",
        message: "Client log exists but could not be opened."
      )
      return false
    }

    if source != nil { stop() }
    currentPath = path
    self.handle = handle
    // Start at the end: past conversations are not ours to read.
    offset = (try? handle.seekToEnd()) ?? 0

    let descriptor = handle.fileDescriptor
    let source = DispatchSource.makeFileSystemObjectSource(
      fileDescriptor: descriptor,
      eventMask: [.extend, .write, .delete, .rename],
      queue: queue
    )
    source.setEventHandler { [weak self] in
      self?.readAppendedLines()
    }
    source.setCancelHandler { try? handle.close() }
    source.resume()
    self.source = source

    LogStore.shared.append(
      level: "info",
      scope: "gamelog",
      message: "Watching client log for trade whispers"
    )
    return true
  }

  func stop() {
    guard let source else { return }
    source.cancel()
    self.source = nil
    currentPath = nil
    queue.sync {
      handle = nil
      partial = ""
    }
    LogStore.shared.append(
      level: "info",
      scope: "gamelog",
      message: "Stopped watching client log"
    )
  }

  private func readAppendedLines() {
    guard let handle else { return }

    let size = (try? handle.seekToEnd()) ?? 0
    if size < offset {
      // The game rotated or truncated the log; follow it from the new start.
      offset = 0
      partial = ""
    }
    guard size > offset else { return }

    try? handle.seek(toOffset: offset)
    guard let data = try? handle.read(upToCount: Int(size - offset)), !data.isEmpty else {
      return
    }
    offset = size

    // The client writes UTF-8 but can emit invalid sequences mid-write.
    let chunk = String(decoding: data, as: UTF8.self)
    let combined = partial + chunk
    var lines = combined.components(separatedBy: "\n")
    // The final element is whatever arrived without a trailing newline.
    partial = lines.popLast() ?? ""

    let complete = lines.filter { !$0.trimmingCharacters(in: .whitespaces).isEmpty }
    guard !complete.isEmpty else { return }

    DispatchQueue.main.async { [weak self] in
      self?.onLines?(complete)
    }
  }
}
