# Troubleshooting

## Development

### `pod install` fails

- Make sure you ran `npm install` at the **repo root** first (the Podfile
  resolves pods out of the hoisted `node_modules`).
- Try `pod repo update` if specs are stale.

### Metro can't resolve `@vaaluation/*` packages

Metro is configured with the repo root in `watchFolders`
(`apps/macos/metro.config.js`). If you changed the layout, update that file.

### Build succeeds but a blank window appears

Metro probably isn't running. Start it with `cd apps/macos && npm start`, then
relaunch the app.

## Using ExileWorth

### The price-check shortcut does nothing

1. Check that Accessibility permission is granted in
   System Settings → Privacy & Security → Accessibility.
2. If you updated ExileWorth and the permission shows as granted but doesn't
   work, remove ExileWorth from the Accessibility list and re-add it — macOS
   ties the grant to the app's code signature.
3. Check the shortcut isn't disabled or conflicting in ExileWorth Settings.

### "No item found on clipboard"

- The game only copies items under the cursor. Hover the item, then press the
  shortcut.
- ExileWorth requires the **English** game client for parsing.

### The overlay appears on the wrong screen or position

- Supported display modes are **Windowed** and **Windowed Fullscreen**.
  Exclusive fullscreen is not supported.
- If you moved the game between monitors, trigger the price check again; the
  overlay re-anchors on each check.

### macOS says the app is damaged or can't be opened

Only install ExileWorth from the official GitHub Releases page. Releases are
signed and notarized; you should never need to remove quarantine attributes.
If Gatekeeper complains about an official download, please open an issue.
