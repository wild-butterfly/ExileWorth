import XCTest

final class HotkeySettingsParsingTests: XCTestCase {
  func testShortcutsOnlyEnabledForTheForegroundGame() {
    XCTAssertTrue(HotkeyCenter.shouldEnableShortcuts(frontmostPID: 123, gamePID: 123))
    XCTAssertFalse(
      HotkeyCenter.shouldEnableShortcuts(frontmostPID: 456, gamePID: 123),
      "A running background game must not intercept typing in another app"
    )
    XCTAssertFalse(HotkeyCenter.shouldEnableShortcuts(frontmostPID: 456, gamePID: nil))
    XCTAssertFalse(HotkeyCenter.shouldEnableShortcuts(frontmostPID: nil, gamePID: 123))
    XCTAssertFalse(HotkeyCenter.shouldEnableShortcuts(frontmostPID: nil, gamePID: nil))
  }

  func testApplyingSettingsOutsideGameLeavesNoGlobalRegistrations() {
    let center = HotkeyCenter.shared
    center.setShortcutsEnabled(false)
    center.applyFromSettings()
    XCTAssertTrue(
      center.currentRegistrations.isEmpty,
      "Ignoring callbacks is insufficient: Carbon would still swallow typing"
    )
  }

  func testParsesDefaultSettingsShape() {
    let json = """
      {
        "leagueId": null,
        "hotkeys": {
          "priceCheck": {
            "keyCode": 2, "characters": "D",
            "control": true, "option": false, "shift": false, "command": false
          },
          "priceCheckPersistent": {
            "keyCode": 2, "characters": "D",
            "control": true, "option": true, "shift": false, "command": false
          },
          "toggleOverlay": null
        },
        "onboardingCompleted": false
      }
      """
    let hotkeys = HotkeyCenter.hotkeysFromSettingsJSON(json)

    XCTAssertEqual(hotkeys.count, 2)
    XCTAssertEqual(hotkeys["priceCheck"]?.displayString, "⌃D")
    XCTAssertEqual(hotkeys["priceCheckPersistent"]?.displayString, "⌃⌥D")
    XCTAssertNil(hotkeys["toggleOverlay"], "null means the shortcut is disabled")
  }

  /// A fresh install has no persisted settings; shortcuts must still work.
  func testMissingOrMalformedJSONFallsBackToDefaults() {
    for json in [nil, "{broken", "{}", "{\"hotkeys\": 3}"] as [String?] {
      let hotkeys = HotkeyCenter.hotkeysFromSettingsJSON(json)
      XCTAssertEqual(hotkeys["priceCheck"]?.displayString, "⌃D")
      XCTAssertEqual(hotkeys["priceCheckPersistent"]?.displayString, "⌃⌥D")
      XCTAssertEqual(hotkeys["toggleOverlay"]?.displayString, "⇧Space")
    }
  }

  /// Once settings exist they win outright — a disabled shortcut must not be
  /// resurrected by the defaults.
  func testExplicitNullIsNotOverriddenByDefaults() {
    let json = "{\"hotkeys\": {\"priceCheck\": null}}"
    XCTAssertNil(HotkeyCenter.hotkeysFromSettingsJSON(json)["priceCheck"])
  }

  func testIgnoresEntriesWithoutKeyCode() {
    let json = "{\"hotkeys\": {\"priceCheck\": {\"characters\": \"D\"}}}"
    XCTAssertTrue(HotkeyCenter.hotkeysFromSettingsJSON(json).isEmpty)
  }
}
