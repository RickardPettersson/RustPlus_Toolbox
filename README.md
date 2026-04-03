# RustPlus Toolbox

A Windows desktop companion app for [Rust](https://rust.facepunch.com/) that connects to your game server in real time. See your server's in-game time, player count, sunrise/sunset schedule, and control Smart Switches — all from a small window on your desktop without having to open the Rust+ mobile app.

On first launch the application walks you through linking your Steam account and pairing a server. After that it remembers your credentials and reconnects automatically every time you start it.

---

## Features

- **Live server time** displayed in a large, easy-to-read clock with a day/night indicator
- **Game time in taskbar** — when the window is minimized, the current in-game time is shown in the taskbar title
- **Player count & queue** updated every 60 seconds
- **Sunrise & sunset times** so you always know when night is coming
- **Smart Switch buttons** — toggle lights, turrets or any smart switch directly from the app, with green/red colour coding for on/off state
- **Real-time entity updates** — switch states update instantly when changed in-game
- **Live entity pairing** — pair new entities in-game while the app is running; Smart Switches appear as buttons immediately without restarting
- **FCM push notification listener** — listens for all Rust+ push notifications (alarms, player events, news, pairings) in the background with auto-reconnect
- **Discord webhook alerts** — send alarm notifications to Discord with customizable messages and user mentions, configurable per server or per individual Smart Alarm
- **Automatic FCM registration** — first-run wizard handles Firebase Cloud Messaging setup, Steam linking, and server pairing
- **Persistent configuration** — credentials and server list are saved locally so you only pair once

---

## Getting Started

### Prerequisites

| Requirement | Notes |
|---|---|
| **Windows 10** (build 19041) or later | The app uses Windows Forms |
| **.NET 10 Runtime** | Download from [dot.net](https://dotnet.microsoft.com/download) |
| **Chromium-based browser** | Chrome or Edge (Edge ships with Windows 10+). Required only during first-run Steam linking — see [Why Chrome or Edge?](#why-chrome-or-edge) |
| **Rust game server** with Rust+ companion enabled | You need to be able to pair via the in-game Rust+ menu |

### Running the application

1. Download the latest release (or build from source — see below).
2. Run **RustPlus_Toolbox.exe**.
3. **First launch only** — the app will:
   - Register with Firebase Cloud Messaging (automatic, takes a few seconds).
   - Open a Chromium-based browser (Chrome or Edge) so you can log in with your Steam account and link it to Rust+.
   - Save your credentials to `rustplus.config.json` so you won't need to repeat this step.
   - Show a dialog asking you to pair a server from the in-game Rust+ menu.
   - Once you pair a server, the connection details are saved to `ServerList.json` and the app connects.
4. **Subsequent launches** — if `rustplus.config.json` exists but `ServerList.json` is empty (e.g. after a wipe), the app skips registration and goes straight to listening for a new server pairing.
5. **Normal startup** — if both config files exist and the server list has entries, the app connects to your server immediately.

### Configuration files

| File | Purpose |
|---|---|
| `rustplus.config.json` | FCM & Steam credentials (created on first run). Delete this file to re-register from scratch. |
| `ServerList.json` | Paired server details — IP, port, Steam ID, player token, entities, and Discord webhook settings. |
| `logs/app-*.log` | Daily rolling log files (kept for 14 days). |

### After a server wipe

When your Rust server wipes, the Rust+ companion settings are reset on the server side. Your local `rustplus.config.json` (FCM/Steam credentials) is still valid, but the server pairing (player token) is no longer accepted.

To reconnect after a wipe:

1. **Close** RustPlus Toolbox.
2. **Delete** `ServerList.json` from the application directory.
3. **Start** RustPlus Toolbox again — it will detect that no servers are configured and prompt you to pair a new server.
4. **Open Rust** and pair your server via the in-game Rust+ companion menu.
5. The app will automatically detect the pairing and save the new server details.

> **Note:** You do **not** need to delete `rustplus.config.json` — your FCM and Steam credentials survive wipes. Only the server list needs to be reset.

---

## ServerList.json Configuration

The `ServerList.json` file stores your paired servers, entities, and notification settings. Below is a full example with all available options:

```json
[
  {
    "id": 1,
    "active": true,
    "name": "My Rust Server",
    "rustPlusConfigPath": "",
    "serverIP": "123.45.67.89",
    "rustPlusPort": 28083,
    "steamId": 76561198012345678,
    "playerToken": 123456789,
    "baseLocationX": 0,
    "baseLocationY": 0,
    "radius": 0,
    "discordWebhook": {
      "webhookUrl": "https://discord.com/api/webhooks/123456/abcdef",
      "message": "\ud83d\udea8 **{title}** \u2014 {message}",
      "userIds": ["123456789012345678", "987654321098765432"]
    },
    "entities": [
      {
        "entityId": 12345,
        "entityType": 1,
        "name": "Base Lights"
      },
      {
        "entityId": 67890,
        "entityType": 2,
        "name": "Front Door Alarm",
        "discordWebhook": {
          "webhookUrl": "https://discord.com/api/webhooks/789012/ghijkl",
          "message": "\ud83c\udfe0 FRONT DOOR: {title} \u2014 {message}",
          "userIds": ["111111111111111111"]
        }
      },
      {
        "entityId": 11111,
        "entityType": 3,
        "name": "TC Monitor"
      }
    ]
  }
]
```

### Server settings

| Field | Type | Description |
|---|---|---|
| `id` | int | Unique server identifier |
| `active` | bool | Whether this is the currently active server |
| `name` | string | Server display name |
| `serverIP` | string | Server IP address |
| `rustPlusPort` | int | Rust+ companion port |
| `steamId` | ulong | Your Steam ID |
| `playerToken` | int | Player token from Rust+ pairing |
| `discordWebhook` | object | Default Discord webhook settings for all alarms on this server (optional) |

### Entity settings

| Field | Type | Description |
|---|---|---|
| `entityId` | uint | The in-game entity ID |
| `entityType` | int | `1` = Smart Switch, `2` = Smart Alarm, `3` = Storage Monitor |
| `name` | string | Display name for the entity |
| `discordWebhook` | object | Entity-specific Discord webhook settings (optional, overrides server defaults) |

### Discord webhook settings

The `discordWebhook` object can be set at the **server level** (applies to all alarms) and/or at the **entity level** (overrides server defaults for that specific alarm). Entity-level settings take priority per field — so you can share a webhook URL across the server but use a different message per alarm.

| Field | Type | Description |
|---|---|---|
| `webhookUrl` | string | Discord webhook URL (create one in Discord: Server Settings > Integrations > Webhooks) |
| `message` | string | Message template. Use `{title}` and `{message}` as placeholders for the alarm notification data. Default: `🚨 **{title}** — {message}` |
| `userIds` | string[] | List of Discord user IDs to mention/tag. Get a user's ID by enabling Developer Mode in Discord (Settings > Advanced), then right-click the user > Copy User ID. |

### Why Chrome or Edge?

During Steam account linking the app serves a local page on `localhost:3000` that opens a popup to the Facepunch Rust+ login page. When the login completes, the Facepunch page calls `window.ReactNativeWebView.postMessage(...)` — a function that the local page injects into the popup window. Because `localhost` and `companion-rust.facepunch.com` are different origins, this cross-origin JavaScript injection is blocked by the browser's same-origin policy.

The only way to make it work is to launch a Chromium-based browser with the `--disable-web-security` flag. Both **Google Chrome** and **Microsoft Edge** support this flag. Edge ships pre-installed on Windows 10 and later, so most users won't need to install anything extra.

The app searches for browsers in this order:

1. Google Chrome
2. Microsoft Edge
3. Chromium (Linux)

If none are found, a clear error message is shown.

---

## Building from Source

### Prerequisites

| Requirement | Notes |
|---|---|
| **.NET 10 SDK** | Download from [dot.net](https://dotnet.microsoft.com/download) |
| **Visual Studio 2022 17.14+** (optional) | With the **.NET desktop development** workload |

### Clone & build

```bash
git clone https://github.com/RickardPettersson/RustPlus_Toolbox.git
cd RustPlus_Toolbox
dotnet restore
dotnet build
```

### Run

```bash
dotnet run --project RustPlus_Toolbox/RustPlus_Toolbox.csproj
```

### Publish a self-contained executable

```bash
dotnet publish RustPlus_Toolbox/RustPlus_Toolbox.csproj -c Release -r win-x64 --self-contained
```

The output will be in `RustPlus_Toolbox/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/`.

---

## Architecture

### Solution structure

```
RustPlus_Toolbox.slnx
  |
  |-- RustPlus_Toolbox/               Windows Forms application (.NET 10)
  |     |-- Program.cs                Entry point, DI & Serilog setup
  |     |-- MainWindow.cs             Main form — UI, Rust+ API, FCM orchestration
  |     |-- MainWindow.Designer.cs    WinForms designer (labels, flow panel)
  |     |-- DiscordWebhookService.cs  Discord webhook notifications for alarms
  |     |-- ArctisNovaOledService.cs  Optional Arctis Nova Pro OLED headset display
  |     |-- Models/
  |     |     |-- ServerItem.cs       Server, entity & Discord webhook data models
  |     |-- ServerList.json           Paired server configuration
  |
  |-- RustPlus_FCM/                   Class library — FCM registration & MCS listener
        |-- GoogleFcm.cs              Firebase/GCM device registration
        |-- McsClient.cs              Google MCS push notification listener (TLS)
        |-- ApiClient.cs              Expo push token & Rust+ Companion API calls
        |-- SteamPairing.cs           Steam OAuth login via local HTTP server + Chromium browser
        |-- RustPlusNotification.cs   Notification data models
        |-- ConfigManager.cs          JSON config read/write
        |-- Protobuf.cs               Lightweight protobuf encoder/decoder
        |-- FileLoggerProvider.cs     File-based ILogger implementation
```

### Application flow

The diagram below shows the step-by-step flow from startup to real-time monitoring.

```
 APPLICATION START
       |
       v
 +---------------------+
 | Program.cs           |
 | - Configure Serilog  |
 | - Build DI host      |
 | - Resolve MainWindow |
 | - Application.Run()  |
 +---------------------+
       |
       v
 +---------------------+
 | SetupServerList()    |
 | Load ServerList.json |
 +---------------------+
       |
       +--- Servers found? ---YES---> [Connect to Rust+ API] (see below)
       |
       NO
       |
       v
 +-------------------------------+
 | rustplus.config.json exists?  |
 +-------------------------------+
       |                   |
      YES                  NO
       |                   |
       |                   v
       |    +--------------------------------------+
       |    | RunFcmRegistrationAsync()            |
       |    |                                      |
       |    |  1. GoogleFcm.RegisterAsync()        |
       |    |     - Generate Firebase ID (FID)     |
       |    |     - Firebase Installation auth     |
       |    |     - Device checkin (protobuf)      |
       |    |     - GCM token registration         |
       |    |                                      |
       |    |  2. ApiClient.GetExpoPushTokenAsync() |
       |    |     - Exchange FCM token for Expo    |
       |    |                                      |
       |    |  3. SteamPairing.LinkSteamAsync()    |
       |    |     - Start local HTTP server :3000  |
       |    |     - Launch Chrome/Edge with        |
       |    |       --disable-web-security         |
       |    |     - Capture Steam auth token       |
       |    |                                      |
       |    |  4. ApiClient.RegisterWithRustPlus() |
       |    |     - Register with Companion API    |
       |    |                                      |
       |    |  5. Save rustplus.config.json        |
       |    +--------------------------------------+
       |                   |
       +---<---<---<---<---+
       |
       v
 +--------------------------------------+
 | ListenForServerPairingAsync()        |
 |                                      |
 |  - Load GCM credentials from config |
 |  - Connect McsClient to Google MCS  |
 |    (mtalk.google.com:5228, TLS)     |
 |  - Wait for pairing notification    |
 |    (channelId=pairing, type=server) |
 |  - Map notification -> ServerItem   |
 |  - Save ServerList.json             |
 +--------------------------------------+
       |
       v
 +--------------------------------------+
 | Connect to Rust+ API                 |
 |                                      |
 |  - WebSocket to server IP:port      |
 |  - Authenticate with SteamId &      |
 |    PlayerToken                      |
 |  - Subscribe to entity events       |
 |  - Load entity states               |
 |  - Create Smart Switch buttons      |
 +--------------------------------------+
       |
       +---> Start FCM Listener (background)
       |       |
       |       v
       |     +--------------------------------------+
       |     | McsClient (persistent connection)    |
       |     |                                      |
       |     |  On alarm notification:             |
       |     |    - Log the alarm                  |
       |     |    - Send Discord webhook           |
       |     |      (server or entity settings)    |
       |     |                                      |
       |     |  On entity pairing:                 |
       |     |    - Add entity to server list      |
       |     |    - Save ServerList.json            |
       |     |    - Create Smart Switch button     |
       |     |                                      |
       |     |  On other notifications:            |
       |     |    - Log (player, news, etc.)       |
       |     |                                      |
       |     |  Auto-reconnects on disconnect      |
       |     +--------------------------------------+
       |
       v
 +--------------------------------------+
 | Main Loop (1-second timer)           |
 |                                      |
 |  Every tick:                        |
 |    - Check/reconnect WebSocket      |
 |    - Interpolate & display time     |
 |    - Show time in taskbar title     |
 |      when minimized                 |
 |                                      |
 |  Every 60 seconds:                  |
 |    - Fetch server time (GetTime)    |
 |    - Calculate time progression     |
 |    - Fetch server info (GetInfo)    |
 |    - Update player count & queue    |
 |                                      |
 |  On entity broadcast:               |
 |    - Update switch button colours   |
 |    - Update entity state in memory  |
 +--------------------------------------+
```

### Key dependencies

| Package | Version | Purpose |
|---|---|---|
| `RustPlusApi` | 1.4.0 | WebSocket client for the Rust+ server protocol |
| `RustPlusApi.Fcm` | 1.4.0 | FCM integration helpers for RustPlusApi |
| `Microsoft.Extensions.Hosting` | 10.0.1 | Dependency injection & host builder |
| `Microsoft.Extensions.Logging` | 10.0.5 | Logging abstractions (used by RustPlus_FCM) |
| `Serilog.Extensions.Hosting` | 10.0.0 | Serilog integration with .NET hosting |
| `Serilog.Sinks.File` | 8.0.0-dev-02318 | Rolling file log output |
| `HidSharp` | 2.6.4 | USB HID communication (Arctis Nova Pro OLED) |
| `SkiaSharp` | 3.119.2 | 2D graphics rendering (Arctis Nova Pro OLED) |

---

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

---

## Copyright

Copyright (c) 2026 Rickard Nordström Pettersson

This project includes the [RustPlus_FCM](https://github.com/RickardPettersson/RustPlus_FCM) library, also by Rickard Nordström Pettersson, licensed under the MIT License.
