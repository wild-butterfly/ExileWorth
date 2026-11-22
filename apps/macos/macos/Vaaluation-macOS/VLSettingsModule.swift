import Foundation
import React
import ServiceManagement

@objc(VLSettings)
final class VLSettingsModule: NSObject {
  @objc static func requiresMainQueueSetup() -> Bool { false }

  @objc(getSettings:rejecter:)
  func getSettings(
    _ resolve: @escaping RCTPromiseResolveBlock,
    rejecter _: @escaping RCTPromiseRejectBlock
  ) {
    resolve(SettingsStore.shared.settingsJSON)
  }

  @objc(setSettings:resolver:rejecter:)
  func setSettings(
    _ json: String,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    guard
      let data = json.data(using: .utf8),
      (try? JSONSerialization.jsonObject(with: data)) is [String: Any]
    else {
      reject("invalid_settings", "Settings payload is not a JSON object", nil)
      return
    }
    SettingsStore.shared.settingsJSON = json
    // Hotkey registrations follow the persisted settings; re-sync natively so
    // shortcuts work even when no React window is open.
    DispatchQueue.main.async {
      HotkeyCenter.shared.applyFromSettings()
      AppDelegate.syncLogWatcher()
    }
    resolve(nil)
  }

  /// Whether macOS opens ExileWorth at login: "enabled", "disabled", or
  /// "requiresApproval" when the user must allow it in System Settings.
  @objc(getOpenAtLogin:rejecter:)
  func getOpenAtLogin(
    _ resolve: @escaping RCTPromiseResolveBlock,
    rejecter _: @escaping RCTPromiseRejectBlock
  ) {
    resolve(Self.loginItemStatus())
  }

  @objc(setOpenAtLogin:resolver:rejecter:)
  func setOpenAtLogin(
    _ enabled: Bool,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    do {
      // The app itself is the login item; no helper bundle is needed.
      if enabled {
        try SMAppService.mainApp.register()
      } else {
        try SMAppService.mainApp.unregister()
      }
      resolve(Self.loginItemStatus())
    } catch {
      reject("login_item", error.localizedDescription, error)
    }
  }

  @objc(openLoginItemsSettings:rejecter:)
  func openLoginItemsSettings(
    _ resolve: @escaping RCTPromiseResolveBlock,
    rejecter _: @escaping RCTPromiseRejectBlock
  ) {
    DispatchQueue.main.async { SMAppService.openSystemSettingsLoginItems() }
    resolve(nil)
  }

  private static func loginItemStatus() -> String {
    switch SMAppService.mainApp.status {
    case .enabled: return "enabled"
    case .requiresApproval: return "requiresApproval"
    default: return "disabled"
    }
  }
}
