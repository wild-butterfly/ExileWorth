import Cocoa

final class AppDelegate: NSObject, NSApplicationDelegate {
  private var reactHost: ReactHost?
  private var statusItemController: StatusItemController?
  private var settingsWindowController: SettingsWindowController?
  private var overlayController: OverlayWindowController?
  private var activationObserver: NSObjectProtocol?

  func applicationDidFinishLaunching(_ notification: Notification) {
    let mainMenu = NSMenu()
    let appMenuItem = NSMenuItem()
    let appMenu = NSMenu(title: "ExileWorth")
    appMenu.addItem(
      withTitle: "Quit ExileWorth",
      action: #selector(NSApplication.terminate(_:)),
      keyEquivalent: "q"
    )
    appMenuItem.submenu = appMenu
    mainMenu.addItem(appMenuItem)
    NSApp.mainMenu = mainMenu

    let host = ReactHost()
    reactHost = host
    statusItemController = StatusItemController(actions: self)

    LogStore.shared.append(level: "info", scope: "app", message: "ExileWorth launched")
    // Recorded at every launch: without it, diagnosing a failed chat command
    // means guessing whether macOS actually trusts this build.
    LogStore.shared.append(
      level: "info",
      scope: "permissions",
      message: "Accessibility: \(PermissionService.accessibilityStatus().rawValue)"
    )

    // Global shortcuts come straight from persisted settings so they work
    // before (and without) any React window existing.
    HotkeyCenter.shared.onHotkey = { [weak self] action in
      self?.handleHotkey(action: action)
    }
    // Shift+Space is easy to press during normal typing. Register shortcuts
    // only while the game is frontmost; ignoring callbacks alone would still
    // swallow the user's keystrokes in other apps.
    activationObserver = NSWorkspace.shared.notificationCenter.addObserver(
      forName: NSWorkspace.didActivateApplicationNotification,
      object: nil,
      queue: .main
    ) { [weak self] _ in
      self?.syncShortcutContext()
      if let game = GameLocator.runningGame(), game.isActive {
        AppDelegate.syncLogWatcher()
      }
    }
    syncShortcutContext()
    let hotkeyErrors = HotkeyCenter.shared.applyFromSettings()
    for (action, error) in hotkeyErrors {
      LogStore.shared.append(
        level: "warn",
        scope: "hotkeys",
        message: "Could not register \(action): \(error.message)"
      )
    }

    // The log watcher belongs to the app, not to a React view. Driving it
    // from component lifecycle meant either window unmounting stopped it for
    // both, so whispers silently went unwatched.
    GameLogWatcher.shared.onLines = { [weak self] lines in
      VLEventsModule.emitLogLines(lines)
      guard lines.contains(where: GameLogWatcher.looksLikeTradeWhisper) else { return }
      LogStore.shared.append(
        level: "info",
        scope: "trade",
        message: "Trade whisper detected in the client log"
      )
      // Surfacing the overlay is what makes the feature visible at all: with
      // no window open there is no React root, so nothing would render and
      // the buffered line would sit unread.
      self?.showTradeOverlay()
    }
    AppDelegate.syncLogWatcher()

    let route = SettingsStore.shared.onboardingCompleted ? "price-check" : "onboarding"
    showSettingsWindow(route: route)
  }

  /// Reopen the desktop window when the user clicks the Dock icon.
  func applicationShouldHandleReopen(
    _ sender: NSApplication,
    hasVisibleWindows flag: Bool
  ) -> Bool {
    showSettingsWindow(route: "price-check")
    return true
  }

  func applicationDidBecomeActive(_ notification: Notification) {
    // Permission grants happen in System Settings; re-check whenever the user
    // comes back to us and let the React side update its UI.
    let status = PermissionService.accessibilityStatus()
    if status != lastLoggedAccessibility {
      lastLoggedAccessibility = status
      LogStore.shared.append(
        level: "info",
        scope: "permissions",
        message: "Accessibility changed to: \(status.rawValue)"
      )
    }
    VLEventsModule.emitPermissions(PermissionService.statusDictionary())
  }

  private var lastLoggedAccessibility: PermissionService.Status?

  private func syncShortcutContext() {
    HotkeyCenter.shared.setShortcutsEnabled(
      HotkeyCenter.shouldEnableShortcuts(
        frontmostPID: NSWorkspace.shared.frontmostApplication?.processIdentifier,
        gamePID: GameLocator.runningGame()?.processIdentifier
      )
    )
  }

  private func handleHotkey(action: String) {
    // Re-check at dispatch time in case focus changed after Carbon queued it.
    syncShortcutContext()
    guard HotkeyCenter.shared.shortcutsEnabled else { return }
    VLEventsModule.emitHotkey(action: action)
    switch action {
    case "priceCheck", "priceCheckPersistent":
      let persistent = action == "priceCheckPersistent"
      ClipboardService.shared.triggerGameCopyAndRead(timeoutMs: 600) { result in
        switch result {
        case .success(let text):
          LogStore.shared.append(
            level: "info",
            scope: "price-check",
            message: "Item copied from game (\(text.count) chars)"
          )
          // Copying is asynchronous: do not raise a result over an app the
          // user switched to while we waited for the game's clipboard update.
          self.syncShortcutContext()
          guard HotkeyCenter.shared.shortcutsEnabled else { return }
          VLEventsModule.emitItemCopied(text: text, persistent: persistent)
          self.showOverlay(pinned: persistent)
        case .failure(let error):
          LogStore.shared.append(
            level: "warn",
            scope: "price-check",
            message: error.message
          )
        }
      }
    case "toggleOverlay":
      overlay().toggle(pinned: false)
    default:
      break
    }
  }

  /// Starts or stops log watching to match the persisted setting. Safe to
  /// call repeatedly; starting an already-running watcher is a no-op.
  static func syncLogWatcher() {
    if SettingsStore.shared.tradeWhispersEnabled {
      GameLogWatcher.shared.start()
    } else {
      GameLogWatcher.shared.stop()
    }
  }

  private func overlay() -> OverlayWindowController {
    if overlayController == nil, let host = reactHost {
      overlayController = OverlayWindowController(reactHost: host)
    }
    return overlayController!
  }

  private func showOverlay(pinned: Bool) {
    overlay().show(pinned: pinned)
  }

  /// Raises the overlay on its Trades tab when a buy request arrives.
  /// Pinned, because a trade needs several deliberate clicks and a panel
  /// that vanished on the first stray click would be worse than useless.
  private func showTradeOverlay() {
    guard SettingsStore.shared.showOverlayOnTradeWhisper else { return }
    let controller = overlay()
    controller.show(pinned: true, anchor: .topRight)
    VLEventsModule.emitShowTrades()
  }

  func applicationWillTerminate(_ notification: Notification) {
    if let activationObserver {
      NSWorkspace.shared.notificationCenter.removeObserver(activationObserver)
    }
    HotkeyCenter.shared.unregisterAll()
    LogStore.shared.append(level: "info", scope: "app", message: "ExileWorth terminating")
  }

  /// - Parameter aboveGame: when true the window floats over Path of Exile
  ///   (including Windowed Fullscreen) instead of behaving like an ordinary
  ///   window that the game can cover.
  private func showSettingsWindow(route: String, aboveGame: Bool = false) {
    guard let host = reactHost else { return }
    if settingsWindowController == nil {
      settingsWindowController = SettingsWindowController(reactHost: host, initialRoute: route)
    }
    settingsWindowController?.setFloatingAboveGame(aboveGame)
    settingsWindowController?.show(route: route)
    NSApp.activate(ignoringOtherApps: true)
  }
}

// MARK: - StatusItemActions

extension AppDelegate: StatusItemActions {
  func openSettings() {
    showSettingsWindow(route: "settings")
  }

  func openLogs() {
    showSettingsWindow(route: "logs")
  }

  func openAbout() {
    showSettingsWindow(route: "about")
  }

  func quit() {
    NSApp.terminate(nil)
  }
}
