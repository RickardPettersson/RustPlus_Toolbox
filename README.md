# RustPlus Toolbox

A Windows desktop companion app for [Rust](https://rust.facepunch.com/) that connects to your game server in real time. See your server's in-game time, player count, sunrise/sunset schedule, and control Smart Switches — all from a small window on your desktop without having to open the Rust+ mobile app.

On first launch the application walks you through linking your Steam account and pairing a server. After that it remembers your credentials and reconnects automatically every time you start it.

---

## Features

- **Live server time** displayed in a large, easy-to-read clock with a day/night indicator
- **Player count & queue** updated every 60 seconds
- **Sunrise & sunset times** so you always know when night is coming
- **Smart Switch buttons** — toggle lights, turrets or any smart switch directly from the app, with green/red colour coding for on/off state
- **Real-time entity updates** — switch states update instantly when changed in-game
- **Automatic FCM registration** — first-run wizard handles Firebase Cloud Messaging setup, Steam linking, and server pairing
- **Persistent configuration** — credentials and server list are saved locally so you only pair once

---

## Getting Started

### Prerequisites

| Requirement | Notes |
|---|---|
| **Windows 10** (build 19041) or later | The app uses Windows Forms |
| **.NET 10 Runtime** | Download from [dot.net](https://dotnet.microsoft.com/download) |
| **Google Chrome** | Required only during first-run Steam linking |
| **Rust game server** with Rust+ companion enabled | You need to be able to pair via the in-game Rust+ menu |

### Running the application

1. Download the latest release (or build from source — see below).
2. Run **RustPlus_Toolbox.exe**.
3. **First launch only** — the app will:
   - Register with Firebase Cloud Messaging (automatic, takes a few seconds).
   - Open Google Chrome so you can log in with your Steam account and link it to Rust+.
   - Show a dialog asking you to pair a server from the in-game Rust+ menu.
   - Once you pair a server, the connection details are saved to `ServerList.json` and the app connects.
4. **Every subsequent launch** — the app loads the saved configuration and connects to your server immediately.

### Configuration files

| File | Purpose |
|---|---|
| `rustplus.config.json` | FCM & Steam credentials (created on first run). Delete this file to re-register. |
| `ServerList.json` | Paired server details — IP, port, Steam ID, player token, entities. |
| `logs/app-*.log` | Daily rolling log files (kept for 14 days). |

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
  |-- RustPlus_Toolbox/          Windows Forms application (.NET 10)
  |     |-- Program.cs           Entry point, DI & Serilog setup
  |     |-- MainWindow.cs        Main form — UI, Rust+ API, FCM orchestration
  |     |-- MainWindow.Designer  WinForms designer (labels, flow panel)
  |     |-- Models/
  |     |     |-- ServerItem.cs  Server & entity data models
  |     |-- ServerList.json      Paired server configuration
  |
  |-- RustPlus_FCM/              Class library — FCM registration & MCS listener
        |-- GoogleFcm.cs         Firebase/GCM device registration
        |-- McsClient.cs         Google MCS push notification listener (TLS)
        |-- ApiClient.cs         Expo push token & Rust+ Companion API calls
        |-- SteamPairing.cs      Steam OAuth login via local HTTP server + Chrome
        |-- RustPlusNotification.cs  Notification data models
        |-- ConfigManager.cs     JSON config read/write
        |-- Protobuf.cs          Lightweight protobuf encoder/decoder
        |-- FileLoggerProvider.cs  File-based ILogger implementation
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
       |    |     - Launch Chrome with login page  |
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
       v
 +--------------------------------------+
 | Main Loop (1-second timer)           |
 |                                      |
 |  Every tick:                        |
 |    - Check/reconnect WebSocket      |
 |    - Interpolate & display time     |
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

---

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

---

## Copyright

Copyright (c) 2026 Rickard Nordström Pettersson

This project includes the [RustPlus_FCM](https://github.com/RickardPettersson/RustPlus_FCM) library, also by Rickard Nordström Pettersson, licensed under the MIT License.
