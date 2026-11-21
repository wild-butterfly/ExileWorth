# ExileWorth

**ExileWorth is a free, open-source Path of Exile 1 (PoE 1) price checker and trade overlay for macOS and Windows.** Hover over an item to compare live asking prices from the official trade service. Filter listings by item modifiers, check currency exchange rates, and respond to trade whispers from the game overlay.

ExileWorth was formerly called Vaaluation.

[Website](https://wild-butterfly.github.io/ExileWorth/) · [Guides](https://wild-butterfly.github.io/ExileWorth/guides/) · [Release notes](https://github.com/wild-butterfly/ExileWorth/releases) · [Report an issue](https://github.com/wild-butterfly/ExileWorth/issues/new/choose)

## Download

| Platform                            | Build                                                                                                                            | Default price check                               | Setup                                                                        |
| ----------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------- | ---------------------------------------------------------------------------- |
| macOS 14+ · Apple silicon and Intel | [Download for Mac](https://github.com/wild-butterfly/ExileWorth/releases/download/v0.1.12-macos/ExileWorth-0.1.12-macOS.zip)     | <kbd>Control</kbd> + <kbd>D</kbd>                 | [Mac setup](https://wild-butterfly.github.io/ExileWorth/guides/install/)     |
| Windows 11 x64 · Preview            | [Download for Windows](https://github.com/wild-butterfly/ExileWorth/releases/download/v0.1.23/ExileWorth-0.1.23-Windows-x64.zip) | <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>D</kbd> | [Windows setup](https://wild-butterfly.github.io/ExileWorth/guides/windows/) |

Select **Covens** for Covens of Umbra.

## See it in game

### macOS Price Check

<img src="docs/screenshots/price-check-detail.png" alt="ExileWorth's macOS Price Check overlay, showing a price estimate, modifier filters, and comparable listings" width="415">

### macOS Trades

![ExileWorth's macOS Trades overlay over Path of Exile, showing a sample Goldrim trade whisper and its available actions](docs/screenshots/trades.png)

_The Trades screenshot shows a simulated whisper._

## Windows desktop preview

![ExileWorth for Windows showing the Price Check screen and the Trades, Currency, and Settings tabs](docs/screenshots/windows-desktop.png)

_Windows 0.1.17 running with sample Divine Orb listings in a [Windows CI run](https://github.com/wild-butterfly/ExileWorth/actions/runs/43709975867)._

## Features

| Feature                   | What you can do                                                                                                                                                                                                            |
| ------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Price Check**           | Compare asking prices from the official trade service and refine the search by modifier.                                                                                                                                   |
| **Trades**                | Enable chat log watching to see incoming trade requests. Send replies, invites, or trade requests with a click. Edit, add, or remove quick replies in Settings.                                                            |
| **Currency**              | Compare exchange rates for currencies such as Divine, Chaos, and Exalted Orbs.                                                                                                                                             |
| **Mac price filters**     | Edit minimums beside modifiers, choose Exact or a percentage preset, and optionally combine elemental resistances. Manual searches retain the chosen minimums.                                                             |
| **Currency history**      | Select either side of a currency pair from the supported list and inspect 24-hour, 7-day, or 30-day history from GGG's completed exchange trades. Longer views sample hourly digests daily; unavailable hours remain gaps. |
| **Windows price filters** | Show only listings priced in Alchemy, Chaos, Exalted, or Divine Orbs.                                                                                                                                                      |

## First launch

- **Mac:** Unzip the signed and notarized build, move `ExileWorth.app` to **Applications**, and open it. Grant Accessibility when prompted so ExileWorth can copy items and send the trade commands you choose. If the app still shows **Permission needed**, [refresh the permission for the installed copy](https://wild-butterfly.github.io/ExileWorth/guides/install/).
- **Windows:** Extract the entire ZIP and run `ExileWorth.exe` from the extracted folder. The default price check copies the item with <kbd>Ctrl</kbd> + <kbd>C</kbd> to avoid triggering the game's Alt-based tooltip pin.
- **Both:** Bring Path of Exile to the front, hover an inventory item, and press your price check shortcut. Change the shortcut in **Settings**. See the [price check guide](https://wild-butterfly.github.io/ExileWorth/guides/price-check/) for modifier filters and search tips.

## Frequently asked questions

### Which Path of Exile client does ExileWorth support?

ExileWorth supports the English PC client for Path of Exile 1. Path of Exile 2 is unsupported. Use Windowed or Windowed Fullscreen mode for the in-game overlay.

### Where do ExileWorth's item prices come from?

Price Check compares current asking prices on the official Path of Exile trade service. Use modifier filters to compare similar items. Listings show what sellers are asking, and final sale prices can differ.

## Privacy

No account or telemetry. Item text is parsed locally, and price searches use the official trade API. Trade watching is optional and starts off by default. When enabled, the chat log is read locally and trade buttons send one command per press.

Read the [privacy statement](docs/PRIVACY.md) for details.

## Development

See the [macOS build and development guide](docs/DEVELOPMENT.md), [Windows build instructions](apps/windows/README.md), and [architecture notes](docs/ARCHITECTURE.md).

```bash
npm ci
npm run typecheck && npm run lint && npm test
```

[Contributing](CONTRIBUTING.md) · [MIT License](LICENSE)

ExileWorth is a community project and is not affiliated with or endorsed by Grinding Gear Games. Path of Exile artwork belongs to Grinding Gear Games. IBM Plex is licensed under the SIL Open Font License.
