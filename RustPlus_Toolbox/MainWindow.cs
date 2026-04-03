using Microsoft.Extensions.Logging;
using RustPlus_Toolbox.Models;
using RustPlusApi;
using RustPlusApi.Data;
using Serilog.Core;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Timer = System.Windows.Forms.Timer;

namespace RustPlus_Toolbox
{
    public partial class MainWindow : Form
    {
        private readonly ILogger<MainWindow> _logger;
        private readonly Timer _timer;
        private readonly ArctisNovaOledService _oled;
        private bool _runningGetData;
        private List<ServerItem> _servers = new List<ServerItem>();
        private RustPlus _rustPlus = null;
        private ServerItem _server = null;
        private uint? _lastNumberOfPlayersOnline = 0;

        // FCM listener for push notifications
        private McsClient? _mcsClient;
        private CancellationTokenSource? _mcsCts;

        // Discord webhook service for alarm notifications
        private readonly DiscordWebhookService _discordWebhook;

        // Time prediction fields
        private DateTime _lastApiFetchTime = DateTime.MinValue;
        private double _lastServerTime;
        private double _dayLengthMinutes;
        private double _sunrise;
        private double _sunset;
        private string _serverName = string.Empty;
        private uint? _playerCount;
        private uint? _maxPlayerCount;
        private uint? _queuedPlayerCount;
        private const int ApiFetchIntervalSeconds = 60;

        // Day/night time rates (in-game hours per real second)
        private double _dayRate;
        private double _nightRate;

        public MainWindow(ILogger<MainWindow> logger)
        {
            InitializeComponent();
            _logger = logger;

            // Try to connect to Arctis Nova Pro OLED (non-blocking, optional)
            _discordWebhook = new DiscordWebhookService(logger);

            _oled = new ArctisNovaOledService(logger);
            if (_oled.TryConnect())
                _logger.LogInformation("Arctis Nova Pro OLED display available.");
            else
                _logger.LogInformation("No Arctis Nova Pro OLED display detected. Continuing without it.");

            SetupServerList();

            // Start timer for the get data function
            _timer = new Timer();
            _timer.Interval = 1000; // 1 second
            _timer.Tick += Timer_Tick_GetdataAsync;
            _timer.Start();
        }

        private async void MainWindow_LoadAsync(object sender, EventArgs e)
        {
            await CheckConnectionToRustPlus();

            // Start FCM listener for push notifications when we have an active server and FCM credentials
            StartFcmListenerIfReady();

            if (_server != null)
            {
                foreach (var sw in _server.Entities.Where(x => x.EntityType == 1))
                {
                    var btn = new Button
                    {
                        Text = sw.Name,
                        AutoSize = true,
                        Margin = new Padding(6),
                        Tag = sw,                 // store the object on the button
                        Name = $"btnEntity_{sw.EntityId}"
                    };

                    btn.Click += async (_, __) =>
                    {
                        var entity = (ServerItemEntity)((Button)btn).Tag!;

                        if (entity.State.HasValue)
                        {
                            await _rustPlus.SetSmartSwitchValueAsync(entity.EntityId, !entity.State.Value).WaitAsync(TimeSpan.FromSeconds(10));
                        }
                        else
                        {
                            await _rustPlus.ToggleSmartSwitchAsync(entity.EntityId).WaitAsync(TimeSpan.FromSeconds(10));
                        }
                    };

                    flowLayoutPanel2.Controls.Add(btn);
                }
            }

            flowLayoutPanel2.ResumeLayout();

            ResizeFormToFitButtons(maxWidth: 500, maxHeight: 800);
        }

        private void ResizeFormToFitButtons(int maxWidth, int maxHeight)
        {
            // Ask layout system for the size it would *like* to be.
            var preferredClient = flowLayoutPanel2.GetPreferredSize(new Size(maxWidth, maxHeight));

            // Add a little breathing room.
            preferredClient.Width += 24;
            preferredClient.Height += 24;

            // Convert desired client size to full window size.
            var targetSize = SizeFromClientSize(preferredClient);

            // Clamp so you don’t create a giant window.
            targetSize.Width = Math.Min(targetSize.Width, maxWidth);
            targetSize.Height = Math.Min(targetSize.Height, maxHeight);

            // Optional: also enforce a minimum.
            targetSize.Width = Math.Max(targetSize.Width, 480);
            targetSize.Height = Math.Max(targetSize.Height, 180);

            Size = targetSize;
        }

        private async Task CheckConnectionToRustPlus()
        {
            // Get server object
            _server = _servers.FirstOrDefault(x => x.Active);

            if (_server != null)
            {
                bool newConnection = false;

                if (_rustPlus == null)
                {
                    _rustPlus = new RustPlus(_server.ServerIP, _server.RustPlusPort, _server.SteamId, _server.PlayerToken, false);
                    _rustPlus.OnStorageMonitorTriggered += _rustPlus_OnStorageMonitorTriggered;
                    _rustPlus.OnSmartSwitchTriggered += _rustPlus_OnSmartSwitchTriggered;
                    _rustPlus.NotificationReceived += _rustPlus_NotificationReceived;
                    _rustPlus.MessageReceived += _rustPlus_MessageReceived;

                    _logger.LogDebug("Connecting to Rust+ API at {ServerIP}:{RustPlusPort}", _server.ServerIP, _server.RustPlusPort);

                    await _rustPlus.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    newConnection = true;
                }
                else
                {
                    if (!_rustPlus.IsConnected())
                    {
                        _logger.LogDebug("Reset connection to Rust+ API at {ServerIP}:{RustPlusPort}", _server.ServerIP, _server.RustPlusPort);
                        _rustPlus = null;
                        await CheckConnectionToRustPlus();
                        return;
                    }
                }

                if (newConnection)
                {
                    _= LoadInfoForEntities(_server);
                }
            }
        }

        private async Task LoadInfoForEntities(ServerItem server)
        {
            foreach (var entity in server.Entities)
            {
                if (entity.EntityType == 1) // Smart Switch
                {
                    var response = await _rustPlus.GetSmartSwitchInfoAsync(entity.EntityId).WaitAsync(TimeSpan.FromSeconds(10));

                    entity.State = response.Data.IsActive;

                    if (flowLayoutPanel2.Controls.OfType<Button>().Any(x => x.Name == $"btnEntity_{entity.EntityId}"))
                    {
                        var btn = flowLayoutPanel2.Controls.OfType<Button>().First(x => x.Name == $"btnEntity_{entity.EntityId}");
                        if (entity.State.Value)
                        {
                            btn.BackColor = Color.LightGreen;
                        }
                        else
                        {
                            btn.BackColor = Color.LightCoral;
                        }
                    }
                }
                else if (entity.EntityType == 2) // Smart Alarm
                {
                    var response = await _rustPlus.GetAlarmInfoAsync(entity.EntityId).WaitAsync(TimeSpan.FromSeconds(10));
                }
                else if (entity.EntityType == 3) // Storage Monitor
                {
                    var response = await _rustPlus.GetStorageMonitorInfoAsync(entity.EntityId).WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
        }

        private void _rustPlus_MessageReceived(object? sender, RustPlusContracts.AppMessage e)
        {
            if (e != null)
            {
                if (e.Broadcast != null)
                {
                    if (e.Broadcast.EntityChanged != null)
                    {
                        if (_server.Entities.Any(x => x.EntityId == e.Broadcast.EntityChanged.EntityId))
                        {
                            var entity = _server.Entities.First(x => x.EntityId == e.Broadcast.EntityChanged.EntityId);
                            entity.State = e.Broadcast.EntityChanged.Payload.Value;

                            if (flowLayoutPanel2.Controls.OfType<Button>().Any(x => x.Name == $"btnEntity_{entity.EntityId}")) {
                                var btn = flowLayoutPanel2.Controls.OfType<Button>().First(x => x.Name == $"btnEntity_{entity.EntityId}");
                                if (entity.State.Value)
                                {
                                    btn.BackColor = Color.LightGreen;
                                }
                                else
                                {
                                    btn.BackColor = Color.LightCoral;
                                }
                            }
                            return;
                        }
                    }
                }

                if (e.Response.Info != null || e.Response.Time != null)
                {
                    return;
                }

                _logger.LogInformation("Message received from Rust+ API: " + System.Text.Json.JsonSerializer.Serialize(e));
            }
        }

        private void _rustPlus_NotificationReceived(object? sender, RustPlusContracts.AppMessage e)
        {
            if (e.Broadcast != null)
            {
                if (e.Broadcast.EntityChanged != null)
                {
                    if (_server.Entities.Any(x => x.EntityId == e.Broadcast.EntityChanged.EntityId))
                    {
                        var entity = _server.Entities.First(x => x.EntityId == e.Broadcast.EntityChanged.EntityId);
                        entity.State = e.Broadcast.EntityChanged.Payload.Value;

                        if (flowLayoutPanel2.Controls.OfType<Button>().Any(x => x.Name == $"btnEntity_{entity.EntityId}"))
                        {
                            var btn = flowLayoutPanel2.Controls.OfType<Button>().First(x => x.Name == $"btnEntity_{entity.EntityId}");
                            if (entity.State.Value)
                            {
                                btn.BackColor = Color.LightGreen;
                            }
                            else
                            {
                                btn.BackColor = Color.LightCoral;
                            }
                        }
                        return;
                    }
                }
            }

            _logger.LogInformation("Notification received from Rust+ API: " + System.Text.Json.JsonSerializer.Serialize(e));
        }

        private void _rustPlus_OnStorageMonitorTriggered(object? sender, RustPlusApi.Data.Events.StorageMonitorEventArg e)
        {
            _logger.LogInformation("Storage Monitor triggered: " + System.Text.Json.JsonSerializer.Serialize(e));
        }

        private void _rustPlus_OnSmartSwitchTriggered(object? sender, RustPlusApi.Data.Events.SmartSwitchEventArg e)
        {
            _logger.LogInformation("Smart Switch triggered: " + System.Text.Json.JsonSerializer.Serialize(e));
        }

        private async void Timer_Tick_GetdataAsync(object? sender, EventArgs e)
        {
            // Prevent overlapping ticks if its still running
            if (_runningGetData)
                return;

            try
            {
                _runningGetData = true;
                
                // Get server object
                var server = _servers.FirstOrDefault(x => x.Active);

                if (server != null)
                {
                    await CheckConnectionToRustPlus();

                    var now = DateTime.UtcNow;
                    var shouldFetchFromApi = (now - _lastApiFetchTime).TotalSeconds >= ApiFetchIntervalSeconds 
                                             || _lastApiFetchTime == DateTime.MinValue;

                    if (shouldFetchFromApi)
                    {
                        await FetchTimeAndInfoFromApiAsync();
                        _lastApiFetchTime = now;
                    }

                    // Update UI with predicted time every tick
                    UpdatePredictedTimeUI();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Timer_Tick_GetdataAsync");
            }
            finally
            {
                _runningGetData = false;
            }
        }

        private async Task FetchTimeAndInfoFromApiAsync()
        {
            var response = await _rustPlus.GetTimeAsync().WaitAsync(TimeSpan.FromSeconds(10));

            if (response.IsSuccess)
            {
                _lastServerTime = response.Data.Time;
                _dayLengthMinutes = response.Data.DayLengthMinutes;
                _sunrise = response.Data.Sunrise;
                _sunset = response.Data.Sunset;
                _lastApiFetchTime = DateTime.UtcNow;

                CalculateTimeRates();

                //_logger.LogDebug(
                //    "Time: {Time:F2}, DayLength: {DayLength} min, Sunrise: {Sunrise}, Sunset: {Sunset}, DayRate: {DayRate:F6}, NightRate: {NightRate:F6} (in-game h/real s)",
                //    _lastServerTime, _dayLengthMinutes, _sunrise, _sunset, _dayRate, _nightRate);
            }

            var responseinfo = await _rustPlus.GetInfoAsync().WaitAsync(TimeSpan.FromSeconds(10));

            if (responseinfo.IsSuccess)
            {
                _serverName = responseinfo.Data.Name;
                _playerCount = responseinfo.Data.PlayerCount;
                _maxPlayerCount = responseinfo.Data.MaxPlayerCount;
                _queuedPlayerCount = responseinfo.Data.QueuedPlayerCount;

                if (_lastNumberOfPlayersOnline != responseinfo.Data.PlayerCount)
                {
                    //_logger.LogDebug("Player count changed: {PlayerCount} / {MaxPlayerCount} - Queue: {QueuedPlayerCount} — Server: {Name}", 
                    //    responseinfo.Data.PlayerCount, responseinfo.Data.MaxPlayerCount, responseinfo.Data.QueuedPlayerCount, responseinfo.Data.Name);
                    
                    _lastNumberOfPlayersOnline = responseinfo.Data.PlayerCount;
                }
            }
        }

        /// <summary>
        /// Computes separate day and night time-progression rates from the server's
        /// DayLengthMinutes, sunrise, and sunset values.
        ///
        /// Rust runs day and night at different speeds:
        ///   - DayLengthMinutes = real minutes for the daytime portion (sunrise → sunset)
        ///   - Night is scaled so the full 24h cycle completes; by default night passes
        ///     roughly 2× faster than day.
        ///
        /// Rates are expressed as in-game hours per real second.
        /// </summary>
        private void CalculateTimeRates()
        {
            if (_dayLengthMinutes <= 0 || _sunrise >= _sunset)
                return;

            double dayHours = _sunset - _sunrise;
            double nightHours = 24.0 - dayHours;

            // Day rate: server tells us exactly how many real minutes daytime takes
            double dayRealSeconds = _dayLengthMinutes * 60.0;
            _dayRate = dayHours / dayRealSeconds;

            // Night rate: Rust default night length = DayLengthMinutes / 3
            // (env.daylength = 30 → night ≈ 10 real minutes).
            // This ratio holds unless the server overrides env.nightlength separately,
            // but the Rust+ API doesn't expose night length directly.
            double nightRealSeconds = (_dayLengthMinutes / 3.0) * 60.0;
            _nightRate = nightHours / nightRealSeconds;
        }

        /// <summary>
        /// Returns the correct time-progression rate for the given in-game hour,
        /// using the day rate during daytime and the night rate during nighttime.
        /// </summary>
        private double GetRateForTime(double inGameHour)
        {
            bool isDay = inGameHour >= _sunrise && inGameHour < _sunset;
            return isDay ? _dayRate : _nightRate;
        }

        private void UpdatePredictedTimeUI()
        {
            if (_lastApiFetchTime == DateTime.MinValue || _dayRate <= 0 || _nightRate <= 0)
                return;

            // Predict current time by stepping forward from last known server time,
            // switching between day/night rate at sunrise/sunset boundaries.
            double elapsedRealSeconds = (DateTime.UtcNow - _lastApiFetchTime).TotalSeconds;
            double predictedTime = PredictServerTime(_lastServerTime, elapsedRealSeconds);

            string time_hhmm = FormatInGameTime(predictedTime);
            string sunrise_hhmm = FormatInGameTime(_sunrise);
            string sunset_hhmm = FormatInGameTime(_sunset);

            bool isDay = predictedTime >= _sunrise && predictedTime < _sunset;
            string dayNightIndicator = isDay ? "☀️" : "🌙";

            lblServerTime.Text = $"Time: {time_hhmm} {dayNightIndicator}";
            lblServerName.Text = $"Server Name: {_serverName}";
            lblNumberOfPlayers.Text = $"Players Online: {_playerCount} / {_maxPlayerCount} - Queue: {_queuedPlayerCount} - Sunrise: {sunrise_hhmm} - Sunset: {sunset_hhmm}";

            // Show game time in title bar when minimized
            if (WindowState == FormWindowState.Minimized)
                Text = $"{time_hhmm} - RustPlus Toolbox";
            else
                Text = "RustPlus Toolbox";

            // Update Arctis Nova Pro OLED display if connected
            if (_oled.IsConnected || _oled.TryConnect())
            {
                _oled.UpdateDisplay(time_hhmm, isDay, sunrise_hhmm, sunset_hhmm);
            }
        }

        /// <summary>
        /// Steps forward from <paramref name="startTime"/> by <paramref name="realSeconds"/>
        /// real seconds, switching between day and night rate at sunrise/sunset boundaries.
        /// </summary>
        private double PredictServerTime(double startTime, double realSeconds)
        {
            double currentTime = startTime;
            double remaining = realSeconds;

            while (remaining > 0.001)
            {
                double rate = GetRateForTime(currentTime);
                bool isDay = currentTime >= _sunrise && currentTime < _sunset;

                // How far (in-game hours) until the next boundary?
                double nextBoundary = isDay ? _sunset : (_sunrise + 24.0);
                double hoursToNextBoundary = nextBoundary - currentTime;
                if (hoursToNextBoundary <= 0)
                    hoursToNextBoundary += 24.0;

                // How many real seconds to reach that boundary at the current rate?
                double realSecondsToNextBoundary = hoursToNextBoundary / rate;

                if (remaining <= realSecondsToNextBoundary)
                {
                    currentTime += remaining * rate;
                    remaining = 0;
                }
                else
                {
                    currentTime += hoursToNextBoundary;
                    remaining -= realSecondsToNextBoundary;
                }

                currentTime %= 24.0;
                if (currentTime < 0) currentTime += 24.0;
            }

            return currentTime;
        }

        private const string FcmConfigFile = "rustplus.config.json";

        private void SetupServerList()
        {
            // Load existing server list if the file exists
            if (File.Exists("ServerList.json"))
            {
                var json = File.ReadAllText("ServerList.json");
                _servers = JsonSerializer.Deserialize<List<ServerItem>>(json) ?? new List<ServerItem>();
            }

            if (_servers.Count == 0)
            {
                _logger.LogInformation("No servers in ServerList.json. Starting FCM setup...");
                _ = EnsureFcmConfigAndListenForPairingAsync();
            }
        }

        /// <summary>
        /// Ensures the FCM config file exists (runs full registration if not),
        /// then listens for a server pairing notification via MCS.
        /// </summary>
        private async Task EnsureFcmConfigAndListenForPairingAsync()
        {
            try
            {
                // ── Step 1: Ensure we have FCM credentials ──────────────────────
                JsonObject fcmConfig;

                if (File.Exists(FcmConfigFile))
                {
                    _logger.LogInformation("FCM config file found ({File}). Skipping registration.", FcmConfigFile);
                    fcmConfig = ConfigManager.ReadConfig(FcmConfigFile);
                }
                else
                {
                    _logger.LogInformation("No FCM config file found. Running full FCM registration...");
                    fcmConfig = await RunFcmRegistrationAsync();

                    if (fcmConfig == null)
                        return; // registration failed, error already shown
                }

                // ── Step 2: Check if server list still needs a server ───────────
                if (_servers.Count > 0)
                {
                    _logger.LogInformation("Server list already has entries. No need to listen for pairing.");
                    return;
                }

                // ── Step 3: Extract GCM credentials from config and listen ──────
                var gcm = fcmConfig["fcm_credentials"]?["gcm"]?.AsObject();
                if (gcm == null)
                {
                    _logger.LogError("FCM config is missing gcm credentials. Delete {File} and restart to re-register.", FcmConfigFile);
                    ShowMessageBoxCentered(
                        $"FCM config is corrupt or missing GCM credentials.\nDelete '{FcmConfigFile}' and restart the application.",
                        "Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var androidId = gcm["androidId"]!.GetValue<string>();
                var securityToken = gcm["securityToken"]!.GetValue<string>();

                _logger.LogInformation("Waiting for a server pairing notification...");
                ShowMessageBoxCentered(
                    "FCM is ready!\n\nPress OK, then open Rust and pair a server via the Rust+ companion menu.\nThe application will automatically detect the pairing.",
                    "Waiting for Server Pairing",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                using var listenCts = new CancellationTokenSource();
                var serverItem = await ListenForServerPairingAsync(androidId, securityToken, listenCts.Token);

                if (serverItem != null)
                {
                    _servers.Add(serverItem);
                    SaveServerList();
                    _logger.LogInformation("Server '{Name}' saved to ServerList.json.", serverItem.Name);

                    flowLayoutPanel2.ResumeLayout();
                    ResizeFormToFitButtons(maxWidth: 500, maxHeight: 800);
                }
                else
                {
                    ShowMessageBoxCentered(
                        "Timed out waiting for a server pairing notification.\nRestart the application to try again.",
                        "Timeout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FCM setup failed.");
                ShowMessageBoxCentered($"FCM setup failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Performs the full FCM registration flow (FCM register, Expo token,
        /// Steam linking, Rust+ API registration) and saves the config to disk.
        /// Returns the saved config JsonObject, or null on failure.
        /// </summary>
        private async Task<JsonObject?> RunFcmRegistrationAsync()
        {
            try
            {
                // 1. Register with FCM
                _logger.LogInformation("Registering with FCM...");
                var fcmCredentials = await GoogleFcm.RegisterAsync(_logger);
                _logger.LogInformation("FCM registration successful.");

                // 2. Get Expo push token
                _logger.LogInformation("Fetching Expo Push Token...");
                var expoPushToken = await ApiClient.GetExpoPushTokenAsync(fcmCredentials.Fcm.Token);
                _logger.LogInformation("Expo Push Token received: {Token}", expoPushToken);

                // 3. Link Steam account via browser
                _logger.LogInformation("Launching Chrome to link Steam account with Rust+...");
                using var cts = new CancellationTokenSource();
                var rustplusAuthToken = await SteamPairing.LinkSteamWithRustPlusAsync(_logger, cts.Token);
                _logger.LogInformation("Steam account linked successfully.");

                // 4. Register with Rust+ Companion API
                _logger.LogInformation("Registering with Rust Companion API...");
                await ApiClient.RegisterWithRustPlusAsync(rustplusAuthToken, expoPushToken);
                _logger.LogInformation("Registered with Rust Companion API.");

                // 5. Build and save config (same format as the Console app)
                var configData = new JsonObject
                {
                    ["fcm_credentials"] = JsonSerializer.SerializeToNode(new
                    {
                        gcm = new
                        {
                            androidId = fcmCredentials.Gcm.AndroidId,
                            securityToken = fcmCredentials.Gcm.SecurityToken,
                            token = fcmCredentials.Gcm.Token,
                        },
                        fcm = new
                        {
                            token = fcmCredentials.Fcm.Token,
                        },
                    }),
                    ["expo_push_token"] = expoPushToken,
                    ["rustplus_auth_token"] = rustplusAuthToken,
                };

                ConfigManager.UpdateConfig(FcmConfigFile, configData);
                _logger.LogInformation("FCM credentials saved to {File}.", FcmConfigFile);

                return ConfigManager.ReadConfig(FcmConfigFile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FCM registration failed.");
                ShowMessageBoxCentered($"FCM registration failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        /// <summary>
        /// Connects to the Google MCS server using the saved GCM credentials
        /// and waits for a Rust+ server pairing notification (up to 5 minutes).
        /// </summary>
        private async Task<ServerItem?> ListenForServerPairingAsync(
            string androidId, string securityToken, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<ServerItem?>();

            using var mcsClient = new McsClient(androidId, securityToken, _logger);

            mcsClient.OnDataReceived += data =>
            {
                try
                {
                    var notification = data.Deserialize<RustPlusNotification>();
                    if (notification is null) return;

                    var body = notification.ParseBody();
                    if (body is null) return;

                    // Only handle server pairing notifications
                    if (notification.ChannelId == "pairing" && body.Type == "server")
                    {
                        _logger.LogInformation(
                            "Server pairing notification received: {Name} at {Ip}:{Port}, PlayerId: {PlayerId}, PlayerToken: {PlayerToken}",
                            body.Name, body.Ip, body.Port, body.PlayerId, body.PlayerToken);

                        var serverItem = new ServerItem
                        {
                            Id = 1,
                            Active = true,
                            Name = body.Name ?? "Unknown Server",
                            RustPlusConfigPath = "",
                            ServerIP = body.Ip ?? "",
                            RustPlusPort = int.TryParse(body.Port, out var port) ? port : 0,
                            SteamId = ulong.TryParse(body.PlayerId, out var steamId) ? steamId : 0,
                            PlayerToken = int.TryParse(body.PlayerToken, out var token) ? token : 0,
                            BaseLocationX = 0,
                            BaseLocationY = 0,
                            Radius = 0,
                            Entities = new List<ServerItemEntity>()
                        };

                        tcs.TrySetResult(serverItem);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing FCM notification.");
                }
            };

            // Start listening in the background — ConnectAsync loops with auto-reconnect
            _ = Task.Run(async () =>
            {
                try
                {
                    await mcsClient.ConnectAsync(cancellationToken);
                }
                catch (OperationCanceledException) { }
            }, cancellationToken);

            // Wait for either the server pairing or a 5-minute timeout
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == tcs.Task)
            {
                return await tcs.Task;
            }

            _logger.LogWarning("Timed out waiting for server pairing notification (5 min).");
            return null;
        }

        /// <summary>
        /// Starts a persistent FCM listener if FCM credentials exist and an active server is configured.
        /// Logs all incoming Rust+ push notifications.
        /// </summary>
        private void StartFcmListenerIfReady()
        {
            // Already running
            if (_mcsClient != null)
                return;

            // Need an active server
            if (_server == null)
                return;

            // Need FCM credentials
            if (!File.Exists(FcmConfigFile))
            {
                _logger.LogDebug("No FCM config file found. FCM listener not started.");
                return;
            }

            var config = ConfigManager.ReadConfig(FcmConfigFile);
            var gcm = config["fcm_credentials"]?["gcm"]?.AsObject();
            if (gcm == null)
            {
                _logger.LogWarning("FCM config is missing GCM credentials. FCM listener not started.");
                return;
            }

            var androidId = gcm["androidId"]?.GetValue<string>();
            var securityToken = gcm["securityToken"]?.GetValue<string>();

            if (string.IsNullOrEmpty(androidId) || string.IsNullOrEmpty(securityToken))
            {
                _logger.LogWarning("FCM GCM credentials are incomplete. FCM listener not started.");
                return;
            }

            _mcsCts = new CancellationTokenSource();
            _mcsClient = new McsClient(androidId, securityToken, _logger);

            _mcsClient.OnDataReceived += OnFcmNotificationReceived;

            _logger.LogInformation("Starting FCM listener for push notifications...");

            _ = Task.Run(async () =>
            {
                try
                {
                    await _mcsClient.ConnectAsync(_mcsCts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "FCM listener stopped unexpectedly.");
                }
            }, _mcsCts.Token);
        }

        /// <summary>
        /// Handles all incoming FCM push notifications from Rust+ and logs them.
        /// </summary>
        private void OnFcmNotificationReceived(JsonObject data)
        {
            try
            {
                var notification = data.Deserialize<RustPlusNotification>();
                if (notification is null)
                {
                    _logger.LogWarning("FCM: Received unreadable notification: {Data}", data.ToJsonString());
                    return;
                }

                var body = notification.ParseBody();
                var channelId = notification.ChannelId ?? "unknown";
                var title = notification.Title ?? "";
                var message = notification.Message ?? "";

                switch (channelId)
                {
                    case "pairing" when body?.Type == "server":
                        _logger.LogInformation(
                            "FCM [{Channel}]: Server pairing — {Name} at {Ip}:{Port}",
                            channelId, body.Name, body.Ip, body.Port);
                        break;

                    case "pairing" when body?.Type == "entity":
                        _logger.LogInformation(
                            "FCM Entity Pairing - Title: {Title}, Entity Type: {EntityType}, Entity ID: {EntityId}, Entity Name: {EntityName} — Server: {Name}",
                                notification.Title, body.EntityType, body.EntityId, body.EntityName, body.Name);
                        HandleEntityPairing(body);
                        break;

                    case "alarm":
                        _logger.LogInformation(
                            "FCM [{Channel}]: {Title} — {Message} — Server: {Name}",
                            channelId, title, message, body?.Name ?? "unknown");
                        _ = HandleAlarmNotificationAsync(body, title, message);
                        break;

                    case "player":
                        _logger.LogInformation(
                            "FCM [{Channel}]: {Title} — {Message} — Server: {Name}",
                            channelId, title, message, body?.Name ?? "unknown");
                        break;

                    case "news":
                        _logger.LogInformation(
                            "FCM [{Channel}]: {Title} — {Message} — Server: {Name}",
                            channelId, title, message, body?.Name ?? "unknown");
                        break;

                    default:
                        _logger.LogInformation(
                            "FCM [{Channel}]: {Title} — {Message} (body: {Body}) — Server: {Name}",
                            channelId, title, message,
                            body != null ? JsonSerializer.Serialize(body) : "null",
                            body?.Name ?? "unknown");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing FCM notification.");
            }
        }

        /// <summary>
        /// Adds a paired entity to the active server, saves the server list,
        /// and creates a UI button if the entity is a Smart Switch.
        /// </summary>
        private void HandleEntityPairing(RustPlusNotificationBody body)
        {
            if (_server == null)
            {
                _logger.LogWarning("FCM: Entity pairing received but no active server configured.");
                return;
            }

            if (!uint.TryParse(body.EntityId, out var entityId))
            {
                _logger.LogWarning("FCM: Entity pairing has invalid EntityId: {EntityId}", body.EntityId);
                return;
            }

            int entityType = int.TryParse(body.EntityType, out var et) ? et : 0;
            string entityName = body.EntityName ?? body.Name ?? $"Entity {entityId}";

            // Check if entity already exists on the server
            if (_server.Entities.Any(e => e.EntityId == entityId))
            {
                _logger.LogInformation("FCM: Entity {EntityId} ({Name}) already exists on server, skipping.", entityId, entityName);
                return;
            }

            // Add the entity to the server
            var entity = new ServerItemEntity
            {
                EntityId = entityId,
                EntityType = entityType,
                Name = entityName
            };

            _server.Entities.Add(entity);
            SaveServerList();
            _logger.LogInformation("FCM: Entity {EntityId} ({Name}, type {Type}) added to server '{Server}' and saved.",
                entityId, entityName, entityType, _server.Name);

            // If it's a Smart Switch (type 1), create a button on the UI thread
            if (entityType == 1)
            {
                if (InvokeRequired)
                    Invoke(() => AddSmartSwitchButton(entity));
                else
                    AddSmartSwitchButton(entity);
            }
        }

        /// <summary>
        /// Creates a toggle button for a Smart Switch entity and adds it to the flow panel.
        /// </summary>
        private void AddSmartSwitchButton(ServerItemEntity entity)
        {
            // Don't add duplicate buttons
            if (flowLayoutPanel2.Controls.OfType<Button>().Any(b => b.Name == $"btnEntity_{entity.EntityId}"))
                return;

            var btn = new Button
            {
                Text = entity.Name,
                AutoSize = true,
                Margin = new Padding(6),
                Tag = entity,
                Name = $"btnEntity_{entity.EntityId}"
            };

            btn.Click += async (_, __) =>
            {
                var ent = (ServerItemEntity)btn.Tag!;

                if (ent.State.HasValue)
                {
                    await _rustPlus.SetSmartSwitchValueAsync(ent.EntityId, !ent.State.Value).WaitAsync(TimeSpan.FromSeconds(10));
                }
                else
                {
                    await _rustPlus.ToggleSmartSwitchAsync(ent.EntityId).WaitAsync(TimeSpan.FromSeconds(10));
                }
            };

            flowLayoutPanel2.Controls.Add(btn);

            // Fetch current state and set button color
            _ = LoadInitialSwitchState(entity, btn);

            ResizeFormToFitButtons(maxWidth: 500, maxHeight: 800);
        }

        /// <summary>
        /// Fetches the current state of a smart switch and updates the button color.
        /// </summary>
        private async Task LoadInitialSwitchState(ServerItemEntity entity, Button btn)
        {
            try
            {
                var response = await _rustPlus.GetSmartSwitchInfoAsync(entity.EntityId).WaitAsync(TimeSpan.FromSeconds(10));
                entity.State = response.Data.IsActive;

                if (InvokeRequired)
                    Invoke(() => btn.BackColor = entity.State.Value ? Color.LightGreen : Color.LightCoral);
                else
                    btn.BackColor = entity.State.Value ? Color.LightGreen : Color.LightCoral;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch initial state for entity {EntityId}", entity.EntityId);
            }
        }

        /// <summary>
        /// Handles an alarm notification by sending a Discord webhook if configured.
        /// Uses entity-specific settings if the alarm entity is found, otherwise server defaults.
        /// </summary>
        private async Task HandleAlarmNotificationAsync(RustPlusNotificationBody? body, string title, string message)
        {
            if (_server == null)
                return;

            // Try to find entity-specific webhook settings
            DiscordWebhookSettings? entitySettings = null;

            if (body?.EntityId != null && uint.TryParse(body.EntityId, out var entityId))
            {
                var entity = _server.Entities.FirstOrDefault(e => e.EntityId == entityId);
                entitySettings = entity?.DiscordWebhook;
            }

            await _discordWebhook.SendAlarmNotificationAsync(
                _server.DiscordWebhook,
                entitySettings,
                title,
                message);
        }

        /// <summary>
        /// Stops the FCM listener and releases resources.
        /// </summary>
        private void StopFcmListener()
        {
            _mcsCts?.Cancel();
            _mcsClient?.Dispose();
            _mcsClient = null;
            _mcsCts?.Dispose();
            _mcsCts = null;
            _logger.LogInformation("FCM listener stopped.");
        }

        private void SaveServerList()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_servers, options);
            File.WriteAllText("ServerList.json", json);
        }

        private static string FormatInGameTime(double inGameHour)
        {
            int h = (int)Math.Floor(inGameHour);
            int m = (int)Math.Round((inGameHour - h) * 60);
            if (m == 60) { h++; m = 0; }
            h = ((h % 24) + 24) % 24;
            return $"{h:00}:{m:00}";
        }

        // ── MessageBox centered on owner form ──────────────────────────────

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hInstance, int threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hHook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hHook, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_CBT = 5;
        private const int HCBT_ACTIVATE = 5;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        /// <summary>
        /// Shows a MessageBox centered on this form instead of centered on the screen.
        /// </summary>
        private DialogResult ShowMessageBoxCentered(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            IntPtr hook = IntPtr.Zero;
            HookProc hookProc = (int nCode, IntPtr wParam, IntPtr lParam) =>
            {
                if (nCode == HCBT_ACTIVATE)
                {
                    // wParam is the handle of the MessageBox window being activated
                    if (GetWindowRect(wParam, out RECT msgRect))
                    {
                        int msgWidth = msgRect.Right - msgRect.Left;
                        int msgHeight = msgRect.Bottom - msgRect.Top;

                        // Center on owner form bounds
                        var ownerBounds = Bounds;
                        int x = ownerBounds.Left + (ownerBounds.Width - msgWidth) / 2;
                        int y = ownerBounds.Top + (ownerBounds.Height - msgHeight) / 2;

                        MoveWindow(wParam, x, y, msgWidth, msgHeight, true);
                    }

                    // Unhook immediately — we only need it once per MessageBox call
                    UnhookWindowsHookEx(hook);
                    hook = IntPtr.Zero;
                }
                return CallNextHookEx(hook, nCode, wParam, lParam);
            };

            // Install a CBT hook on the current (UI) thread
            hook = SetWindowsHookEx(WH_CBT, hookProc, IntPtr.Zero, GetCurrentThreadId());

            // Show the MessageBox — the hook fires before it becomes visible
            var result = MessageBox.Show(this, text, caption, buttons, icon);

            // Safety net: unhook if it wasn't consumed (shouldn't happen)
            if (hook != IntPtr.Zero)
                UnhookWindowsHookEx(hook);

            return result;
        }
    }
}
