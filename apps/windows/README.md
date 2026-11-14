# ExileWorth for Windows (preview)

This portable x64 build is for Windows 11 and the English **Path of Exile 1**
client. It includes its runtimes. Extract the entire ZIP into a folder and run
`ExileWorth.exe` from that folder. This preview is unsigned.

1. Use Windowed or Windowed Fullscreen in Path of Exile.
2. With the game in front, hover an inventory item and press **Ctrl + Alt + D**.
   ExileWorth sends the game's advanced item-copy shortcut (**Ctrl + Alt + C**)
   once. It leaves that text on the Windows clipboard.
3. Check the league selector. **Covens** is the PC trade league for Covens of
   Umbra. If a saved league has ended, ExileWorth selects the current league.
   A valid selection such as Standard is kept.
4. Adjust the modifier checkboxes and click **Apply filters**. Listings show
   sellers' asking prices. **Open trade search** opens the matching query.

If the shortcut is unavailable, manually copy an item while hovering it and
click **Check clipboard** in ExileWorth. Keep both apps at the same privilege
level, preferably with neither running as administrator. Closing the window
hides it to the notification area; use the tray menu to reopen it or quit.

Open **Settings** in the desktop window to change the shortcut, trade league, or
where the overlay opens. Select **Save settings** to apply shortcut and placement
changes. Open **Currency** and select **Refresh rates** to see median bulk asking
prices in Chaos Orbs for the selected league.

Incoming trade requests and trade history are currently macOS features.

## Build from source

On Windows or macOS, install Node.js 22 and the .NET 10 SDK. From the repository
root, run `npm ci`, then `npm run package:windows`. The ZIP is written to
`dist/ExileWorth-Windows-x64.zip`. Publishing on macOS can compile the Windows
executable; the tray, clipboard, and game shortcut must be tested on Windows.
