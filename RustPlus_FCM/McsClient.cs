// Copyright (c) 2026 Rickard Nordström Pettersson. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Source: https://github.com/RickardPettersson/RustPlus_FCM

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Client for Google's Mobile Connection Server (MCS) protocol.
/// Connects via TLS to mtalk.google.com:5228 and receives push notifications
/// using protobuf-encoded messages.
/// </summary>
public sealed class McsClient : IDisposable
{
    private const string McsHost = "mtalk.google.com";
    private const int McsPort = 5228;
    private const byte McsVersion = 41;

    // MCS message tags
    private const int TagHeartbeatPing = 0;
    private const int TagHeartbeatAck = 1;
    private const int TagLoginRequest = 2;
    private const int TagLoginResponse = 3;
    private const int TagClose = 4;
    private const int TagDataMessageStanza = 8;

    private static readonly TimeSpan DefaultInactivityTimeout = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly string _androidId;
    private readonly string _securityToken;
    private readonly ILogger _logger;
    private TcpClient? _tcpClient;
    private SslStream? _sslStream;

    /// <summary>Maximum time to wait for any message before triggering a reconnect.</summary>
    public TimeSpan InactivityTimeout { get; set; } = DefaultInactivityTimeout;

    /// <summary>Delay between reconnect attempts.</summary>
    public TimeSpan ReconnectDelay { get; set; } = DefaultReconnectDelay;

    public event Action<JsonObject>? OnDataReceived;

    public McsClient(string androidId, string securityToken, ILogger logger)
    {
        _androidId = androidId;
        _securityToken = securityToken;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectInternalAsync(cancellationToken);
                // Normal return means server closed or inactivity timeout
                _logger.LogWarning("Connection ended, reconnecting in {Delay}s...", (int)ReconnectDelay.TotalSeconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Connection error, reconnecting in {Delay}s...", (int)ReconnectDelay.TotalSeconds);
            }

            Disconnect();
            await Task.Delay(ReconnectDelay, cancellationToken);
        }
    }

    private async Task ConnectInternalAsync(CancellationToken cancellationToken)
    {
        // Perform a checkin with existing credentials before connecting (as the Node.js client does)
        _logger.LogDebug("Performing checkin with androidId={AndroidId}", _androidId);
        await GoogleFcm.PerformCheckinAsync(_androidId, _securityToken);
        _logger.LogDebug("Checkin successful");

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(McsHost, McsPort, cancellationToken);
        _logger.LogDebug("Connected to {Host}:{Port}", McsHost, McsPort);

        _sslStream = new SslStream(_tcpClient.GetStream(), leaveInnerStreamOpen: false);
        await _sslStream.AuthenticateAsClientAsync(McsHost);
        _logger.LogDebug("TLS handshake complete");

        // Send MCS version byte
        await _sslStream.WriteAsync(new[] { McsVersion }, cancellationToken);
        await _sslStream.FlushAsync(cancellationToken);

        // Send LoginRequest
        _logger.LogDebug("Sending LoginRequest...");
        await SendLoginRequestAsync(cancellationToken);
        _logger.LogDebug("LoginRequest sent");

        // Read MCS version from server
        var versionBuf = new byte[1];
        await ReadExactAsync(_sslStream, versionBuf, cancellationToken);
        _logger.LogDebug("Server MCS version: {Version}", versionBuf[0]);

        // Read LoginResponse
        var (tag, _) = await ReadMessageAsync(cancellationToken);
        if (tag != TagLoginResponse)
        {
            throw new InvalidOperationException(
                $"Expected LoginResponse (tag {TagLoginResponse}), got tag {tag}");
        }
        _logger.LogInformation("LoginResponse received — login successful, entering message loop");

        // Enter message loop
        await MessageLoopAsync(cancellationToken);
    }

    private async Task SendLoginRequestAsync(CancellationToken cancellationToken)
    {
        // Convert androidId to hex for device_id field (matching Node.js: Long.fromString(androidId).toString(16))
        var hexAndroidId = long.Parse(_androidId).ToString("x");

        // Setting submessage: name="new_vc", value="1"
        var setting = new ProtobufWriter();
        setting.WriteString(1, "new_vc");
        setting.WriteString(2, "1");

        // LoginRequest proto fields — numbers must match mcs.proto field definitions
        var login = new ProtobufWriter();
        login.WriteString(1, "chrome-63.0.3234.0");                             // field 1: id
        login.WriteString(2, "mcs.android.com");                                // field 2: domain
        login.WriteString(3, _androidId);                                       // field 3: user (decimal androidId)
        login.WriteString(4, _androidId);                                       // field 4: resource (decimal androidId)
        login.WriteString(5, _securityToken);                                   // field 5: auth_token
        login.WriteString(6, $"android-{hexAndroidId}");                        // field 6: device_id (hex)
        login.WriteMessage(8, setting);                                         // field 8: setting
        login.WriteBool(12, false);                                             // field 12: adaptive_heartbeat
        login.WriteBool(14, true);                                              // field 14: use_rmq2
        login.WriteInt32(16, 2);                                                // field 16: auth_service = ANDROID_ID
        login.WriteInt32(17, 1);                                                // field 17: network_type

        _logger.LogDebug("LoginRequest: id=chrome-63.0.3234.0, domain=mcs.android.com, user={User}, resource={Resource}, device_id=android-{DeviceId}, auth_service=2, network_type=1, use_rmq2=true",
            _androidId, _androidId, hexAndroidId);

        await SendMessageAsync(TagLoginRequest, login.ToArray(), cancellationToken);
    }

    private async Task SendMessageAsync(int tag, byte[] data, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        ms.WriteByte((byte)tag);

        // Write varint length
        var length = (uint)data.Length;
        while (length > 0x7F)
        {
            ms.WriteByte((byte)((length & 0x7F) | 0x80));
            length >>= 7;
        }
        ms.WriteByte((byte)length);

        ms.Write(data);

        var bytes = ms.ToArray();
        await _sslStream!.WriteAsync(bytes, cancellationToken);
        await _sslStream.FlushAsync(cancellationToken);
    }

    private async Task<(int tag, byte[] data)> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var tagBuf = new byte[1];
        await ReadExactAsync(_sslStream!, tagBuf, cancellationToken);
        var tag = tagBuf[0];

        var length = await ReadVarintAsync(cancellationToken);

        var data = new byte[length];
        if (length > 0)
        {
            await ReadExactAsync(_sslStream!, data, cancellationToken);
        }

        return (tag, data);
    }

    private async Task<int> ReadVarintAsync(CancellationToken cancellationToken)
    {
        int result = 0;
        int shift = 0;
        var buf = new byte[1];
        while (true)
        {
            await ReadExactAsync(_sslStream!, buf, cancellationToken);
            result |= (buf[0] & 0x7F) << shift;
            if ((buf[0] & 0x80) == 0) break;
            shift += 7;
        }
        return result;
    }

    private async Task MessageLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(InactivityTimeout);

            (int tag, byte[] data) message;
            try
            {
                message = await ReadMessageAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("No messages received for {Timeout}, triggering reconnect", InactivityTimeout);
                return;
            }

            _logger.LogDebug("Received message with tag: {Tag}", message.tag);

            switch (message.tag)
            {
                case TagHeartbeatPing:
                    await SendHeartbeatAckAsync(cancellationToken);
                    break;

                case TagDataMessageStanza:
                    HandleDataMessage(message.data);
                    break;

                case TagClose:
                    _logger.LogWarning("Server closed connection.");
                    return;

                default:
                    // Ignore other message types (IqStanza, etc.)
                    break;
            }
        }
    }

    private async Task SendHeartbeatAckAsync(CancellationToken cancellationToken)
    {
        await SendMessageAsync(TagHeartbeatAck, [], cancellationToken);
    }

    private void HandleDataMessage(byte[] data)
    {
        try
        {
            ParseAndEmitDataMessage(data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse DataMessageStanza ({Length} bytes)", data.Length);
        }
    }

    private void ParseAndEmitDataMessage(byte[] data)
    {
        var reader = new ProtobufReader(data);
        var appData = new JsonObject();
        string? category = null;
        string? persistentId = null;

        while (reader.HasData)
        {
            var (fieldNumber, wireType) = reader.ReadTag();
            switch (fieldNumber)
            {
                case 5 when wireType == 2: // category
                    category = reader.ReadString();
                    break;

                case 7 when wireType == 2: // app_data (repeated AppData message)
                    var subBytes = reader.ReadBytes();
                    var subReader = new ProtobufReader(subBytes);
                    string? key = null;
                    string? value = null;
                    while (subReader.HasData)
                    {
                        var (sf, sw) = subReader.ReadTag();
                        switch (sf)
                        {
                            case 1 when sw == 2: key = subReader.ReadString(); break;
                            case 2 when sw == 2: value = subReader.ReadString(); break;
                            default: subReader.Skip(sw); break;
                        }
                    }
                    if (key is not null)
                    {
                        appData[key] = value;
                    }
                    break;

                case 9 when wireType == 2: // persistent_id (field 9 per mcs.proto)
                    persistentId = reader.ReadString();
                    break;

                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        var notification = new JsonObject
        {
            ["category"] = category,
            ["persistentId"] = persistentId,
            ["appData"] = appData,
        };

        OnDataReceived?.Invoke(notification);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("Connection closed by server.");
            offset += read;
        }
    }

    private void Disconnect()
    {
        _sslStream?.Dispose();
        _sslStream = null;
        _tcpClient?.Dispose();
        _tcpClient = null;
    }

    public void Dispose()
    {
        Disconnect();
    }
}
