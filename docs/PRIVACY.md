# ExileWorth Privacy Statement

ExileWorth is designed so that there is almost nothing to have a privacy policy
about.

## What ExileWorth collects

Nothing. ExileWorth has **no telemetry, no analytics, no crash reporting, and
no accounts**. No data about you or your usage is transmitted to the ExileWorth
project.

## Website analytics

The statements above describe the desktop apps. The ExileWorth website separately
uses optional Google Analytics to measure visits and download-link clicks, only
after a visitor accepts analytics. It does not measure app installations or app
usage. See the [website privacy notice](https://wild-butterfly.github.io/ExileWorth/privacy/).

## Network requests

ExileWorth talks to official Path of Exile services, plus one community data file for item names:

| Request          | When                                                | What is sent                                                                                                                                                               |
| ---------------- | --------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| League list      | On launch and on manual refresh                     | Nothing personal — a plain GET request.                                                                                                                                    |
| Trade search     | Only when you run a price check search              | The item filters you confirmed (base type, modifiers, ranges) and the selected league.                                                                                     |
| Currency history | When you open Currency or change its history period | Completed-hour timestamps requested from GGG's public `web.poecdn.com/api/currency-exchange` feed. Currency and league selection are applied locally.                      |
| Item names       | The first time you open Currency in a session       | A plain GET for `repoe-fork.github.io/base_items.min.json`, the community RePoE table of item names extracted from the game (data owned by GGG). Nothing personal is sent. |

The Currency page prices every Currency Exchange item from completed trades in
GGG's hourly digests: the price is the volume-weighted average over the last 24
hours, and Divine-side trades are converted at the same hours' Divine price. The
item name table is downloaded at runtime rather than shipped with the app, so no
GGG-owned data is redistributed.

Currency history uses completed trades from GGG's hourly digests. Digests are
cached in memory and reused when you change currency pairs. The 7-day and 30-day
views sample hourly digests once per day and include the latest completed hour;
missing hours remain gaps. Live currency offers and historical traded averages
are labelled separately.

Every request carries a descriptive `User-Agent` identifying ExileWorth, as
required by Grinding Gear Games' developer policy.

## Currency icons

The Currency page shows Path of Exile's own orb art, loaded on demand from
Grinding Gear Games' content network (`web.poecdn.com`) using the image paths
their trade API publishes. The art is theirs and is never redistributed with
ExileWorth. These requests carry no identifying information beyond a normal
image fetch.

## Clipboard

- ExileWorth reads the clipboard when you use the price-check shortcut or
  explicitly ask it to check copied text.
- Text that does not look like a Path of Exile item is discarded immediately.
- On macOS, previous clipboard contents are restored where reasonably safe.
  On Windows, the game’s copied item text stays on the clipboard.
- Clipboard contents are never stored, never logged (unless you explicitly
  enable debug logging, which is local-only), and never uploaded.

## Trade whispers (macOS, optional and off by default)

The Trades feature watches the Path of Exile client log for incoming trade
whispers. It is **disabled until you turn it on**, because that log also
contains your private conversations.

When enabled:

- Reading begins at the **end** of the log. Existing chat history is never read.
- Only lines matching the game's own trade-whisper wording are kept. Every
  other line — personal whispers, guild, party, global chat — is discarded
  immediately and is never stored, logged, or displayed.
- Recognized requests live in memory only, and are lost when you quit.
- Nothing from the log is ever uploaded.

Chat commands (invite, trade, kick, whisper, hideout) are sent only when you
press the corresponding button, and each press sends exactly one command —
the same one you would have typed. ExileWorth never sends a command on its
own, never chains commands together, and never replies automatically.

## Local data

Settings (league, shortcuts, overlay position) are stored locally in
`~/Library` under the app's own container. Logs are local, rotated, and never
contain full item text unless debug logging is explicitly enabled. On Windows,
only the selected league is stored in `%LOCALAPPDATA%\Vaaluation\league.txt`.

## Credentials

The MVP requires no authentication at all. ExileWorth will never ask for your
Path of Exile password or your `POESESSID` cookie. If account features are ever
added, they will use the official OAuth flow, and tokens will be stored in the
macOS Keychain.

## macOS permissions

| Permission    | Purpose                                                                                                                                    |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| Accessibility | Send the single item-copy keystroke when you press the price-check shortcut, and send a single chat command when you press a trade button. |

ExileWorth does not request Screen Recording, Input Monitoring, camera,
microphone, location, or contacts access.
