# ExileWorth for Windows (preview)

This portable x64 build is for Windows 11 and the English **Path of Exile 1**
client. It includes its runtimes. Extract the entire ZIP into a folder and run
`ExileWorth.exe` from that folder. This preview is unsigned.

1. Use Windowed or Windowed Fullscreen in Path of Exile.
2. With the game in front, hover an inventory item and press **Alt + D**.
   ExileWorth sends **Ctrl + C** once. Enable **Settings → Item copy → Use advanced item copy** for extra modifier details. Advanced copy uses Alt and may pin the game’s tooltip. It leaves that text on the Windows clipboard.
3. Check the league selector. **Covens** is the PC trade league for Covens of
   Umbra. If a saved league has ended, ExileWorth selects the current league.
   A valid selection such as Standard is kept.
4. The compact overlay opens next to the item without taking focus from the
   game. It shows the typical asking price, the item's modifiers, and the
   cheapest listings. Untick or tick modifiers and select **Search** to refine
   the prices. The **All ▾** menu limits listings to Alchemy, Chaos, Exalted
   or Divine prices. Click a listing to copy its whisper. Drag the top bar to
   move the panel.
5. The overlay's **Trades** tab lists recent trade whispers from the game's
   chat log. **Invite**, **Trade**, **Thanks** and **Kick** each type exactly
   one chat command into the game; **Reply** shows your prepared replies and
   **Done** dims the request. Close game chat first.

In the desktop window, **Price Check** shows the same result with modifier
filters you can adjust before clicking **Search**. **Open trade site** opens
the matching query. If the shortcut is unavailable, copy an item while hovering
it and click **Check clipboard**. Keep both apps at the same privilege level,
preferably with neither running as administrator. Closing the window hides it
to the notification area; use the tray menu to reopen it or quit.

**Trades** shows recent whispers and copies chat commands to the clipboard.
**Currency** loads median bulk asking prices, with icons, in Chaos, Divine or
Exalted Orbs. **Settings** changes the shortcut, trade league, overlay
placement, accent colour, the Thanks message and prepared replies, and whether
ExileWorth starts with Windows.
Changes apply immediately. Use **Ctrl + 1** through **Ctrl + 5** to switch pages.

Trade history is currently a macOS feature.

## Build from source

On Windows or macOS, install Node.js 22 and the .NET 10 SDK. From the repository
root, run `npm ci`, then `npm run package:windows`. The ZIP is written to
`dist/ExileWorth-Windows-x64.zip`. Publishing on macOS can compile the Windows
executable; the tray, clipboard, and game shortcut must be tested on Windows.
