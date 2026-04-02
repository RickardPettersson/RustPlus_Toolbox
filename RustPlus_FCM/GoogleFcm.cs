// Copyright (c) 2026 Rickard Nordström Pettersson. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Source: https://github.com/RickardPettersson/RustPlus_FCM

using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public record FcmCredentials(GcmCredentials Gcm, FcmTokenInfo Fcm);
public record GcmCredentials(string AndroidId, string SecurityToken, string Token);
public record FcmTokenInfo(string Token);

public static class GoogleFcm
{
    public const string ApiKey = "AIzaSyB5y2y-Tzqb4-I4Qnlsh_9naYv_TD8pCvY";
    public const string ProjectId = "rust-companion-app";
    public const string GcmSenderId = "976529667804";
    public const string GmsAppId = "1:976529667804:android:d6f1ddeb4403b338fea619";
    public const string AndroidPackageName = "com.facepunch.rust.companion";
    public const string AndroidPackageCert = "E28D05345FB78A7A1A63D70F4A302DBF426CA5AD";

    public static async Task<FcmCredentials> RegisterAsync(ILogger logger)
    {
        // Step 1: Get Firebase Installation auth token
        var fid = GenerateFid();
        logger.LogDebug("Requesting Firebase Installation auth token...");
        var installationAuthToken = await FirebaseInstallAsync(
            ApiKey, ProjectId, GmsAppId, AndroidPackageName, AndroidPackageCert, fid);
        logger.LogDebug("Firebase Installation auth token received");

        // Step 2: Initial checkin to obtain device credentials
        var (androidId, securityToken) = await CheckinAsync(null, null);
        logger.LogDebug("Checkin successful: androidId={AndroidId}", androidId);

        // Step 3: Confirmation checkin with the obtained credentials
        await CheckinAsync(androidId, securityToken);
        logger.LogDebug("Confirmation checkin successful");

        // Step 4: Register with GCM using the Firebase auth token
        var gcmToken = await GcmRegisterAsync(
            androidId, securityToken, installationAuthToken,
            GcmSenderId, GmsAppId,
            AndroidPackageName, AndroidPackageCert);
        logger.LogDebug("GCM Token: {GcmToken}", gcmToken);

        return new FcmCredentials(
            new GcmCredentials(androidId, securityToken, gcmToken),
            new FcmTokenInfo(gcmToken));
    }

    /// <summary>
    /// Performs a checkin with existing credentials before MCS connect (matching Node.js client behavior).
    /// The response is not parsed — only the HTTP success status matters.
    /// </summary>
    public static async Task PerformCheckinAsync(string androidId, string securityToken)
    {
        var requestBytes = BuildCheckinRequest(androidId, securityToken);

        using var httpClient = new HttpClient();
        var content = new ByteArrayContent(requestBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        var response = await httpClient.PostAsync("https://android.clients.google.com/checkin", content);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<(string androidId, string securityToken)> CheckinAsync(
        string? existingAndroidId, string? existingSecurityToken)
    {
        var requestBytes = BuildCheckinRequest(existingAndroidId, existingSecurityToken);

        using var httpClient = new HttpClient();
        var content = new ByteArrayContent(requestBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        var response = await httpClient.PostAsync("https://android.clients.google.com/checkin", content);
        response.EnsureSuccessStatusCode();

        var responseBytes = await response.Content.ReadAsByteArrayAsync();
        var reader = new ProtobufReader(responseBytes);

        long androidId = 0;
        long securityToken = 0;

        while (reader.HasData)
        {
            var (fieldNumber, wireType) = reader.ReadTag();
            switch (fieldNumber)
            {
                case 7 when wireType == 1: // androidId (fixed64)
                    androidId = (long)reader.ReadFixed64();
                    break;
                case 8 when wireType == 1: // securityToken (fixed64)
                    securityToken = (long)reader.ReadFixed64();
                    break;
                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        if (androidId == 0 || securityToken == 0)
        {
            throw new InvalidOperationException("Checkin failed: missing androidId or securityToken in response.");
        }

        return (androidId.ToString(), securityToken.ToString());
    }

    /// <summary>
    /// Builds a Chrome-type checkin request matching the Node.js push-receiver implementation.
    /// </summary>
    private static byte[] BuildCheckinRequest(string? androidId, string? securityToken)
    {
        // ChromeBuildProto
        var chromeBuild = new ProtobufWriter();
        chromeBuild.WriteInt32(1, 2);               // platform = PLATFORM_MAC
        chromeBuild.WriteString(2, "63.0.3234.0");  // chrome_version
        chromeBuild.WriteInt32(3, 1);               // channel = CHANNEL_STABLE

        // AndroidCheckinProto
        var checkinMsg = new ProtobufWriter();
        checkinMsg.WriteInt32(12, 3);               // type = DEVICE_CHROME_BROWSER
        checkinMsg.WriteMessage(13, chromeBuild);   // chrome_build

        // AndroidCheckinRequest
        var request = new ProtobufWriter();
        if (androidId is not null)
            request.WriteInt64(2, long.Parse(androidId));                     // field 2: id
        request.WriteMessage(4, checkinMsg);                                  // field 4: checkin
        if (securityToken is not null)
            request.WriteFixed64(13, (ulong)long.Parse(securityToken));       // field 13: security_token
        request.WriteInt32(14, 3);                                            // field 14: version
        request.WriteInt32(22, 0);                                            // field 22: user_serial_number

        return request.ToArray();
    }

    /// <summary>
    /// Calls the Firebase Installations API to obtain an auth token.
    /// Matches Node.js AndroidFCM.installRequest().
    /// </summary>
    private static async Task<string> FirebaseInstallAsync(
        string apiKey, string projectId, string gmsAppId,
        string androidPackageName, string androidPackageCert, string fid)
    {
        var payload = new
        {
            fid,
            appId = gmsAppId,
            authVersion = "FIS_v2",
            sdkVersion = "a:17.0.0",
        };

        using var httpClient = new HttpClient();
        var jsonContent = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Android-Package", androidPackageName);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Android-Cert", androidPackageCert);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-firebase-client",
            "android-min-sdk/23 fire-core/20.0.0 device-name/a21snnxx device-brand/samsung " +
            "device-model/a21s android-installer/com.android.vending fire-android/30 " +
            "fire-installations/17.0.0 fire-fcm/22.0.0 android-platform/ kotlin/1.9.23 android-target-sdk/34");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-firebase-client-log-type", "3");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-goog-api-key", apiKey);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Dalvik/2.1.0 (Linux; U; Android 11; SM-A217F Build/RP1A.200720.012)");

        var url = $"https://firebaseinstallations.googleapis.com/v1/projects/{projectId}/installations";
        var response = await httpClient.PostAsync(url, jsonContent);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = responseJson.GetProperty("authToken").GetProperty("token").GetString();

        return token ?? throw new InvalidOperationException("Firebase Installation response missing authToken.");
    }

    private static async Task<string> GcmRegisterAsync(
        string androidId, string securityToken, string installationAuthToken,
        string gcmSenderId, string gmsAppId,
        string androidPackageName, string androidPackageCert)
    {
        var formData = new Dictionary<string, string>
        {
            ["device"] = androidId,
            ["app"] = androidPackageName,
            ["cert"] = androidPackageCert,
            ["app_ver"] = "1",
            ["X-subtype"] = gcmSenderId,
            ["X-app_ver"] = "1",
            ["X-osv"] = "29",
            ["X-cliv"] = "fiid-21.1.1",
            ["X-gmsv"] = "220217001",
            ["X-scope"] = "*",
            ["X-Goog-Firebase-Installations-Auth"] = installationAuthToken,
            ["X-gms_app_id"] = gmsAppId,
            ["X-Firebase-Client"] =
                "android-min-sdk/23 fire-core/20.0.0 device-name/a21snnxx device-brand/samsung " +
                "device-model/a21s android-installer/com.android.vending fire-android/30 " +
                "fire-installations/17.0.0 fire-fcm/22.0.0 android-platform/ kotlin/1.9.23 android-target-sdk/34",
            ["X-Firebase-Client-Log-Type"] = "1",
            ["X-app_ver_name"] = "1",
            ["target_ver"] = "31",
            ["sender"] = gcmSenderId,
        };

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Authorization", $"AidLogin {androidId}:{securityToken}");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Content-Type", "application/x-www-form-urlencoded");

        var content = new FormUrlEncodedContent(formData);
        var response = await httpClient.PostAsync("https://android.clients.google.com/c2dm/register3", content);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync();

        if (responseText.Contains("Error"))
        {
            throw new InvalidOperationException($"GCM register failed: {responseText}");
        }

        // Response format: "token=<gcm_token>"
        var parts = responseText.Split('=', 2);
        if (parts.Length == 2 && parts[0] == "token")
        {
            return parts[1];
        }

        throw new InvalidOperationException($"Unexpected GCM register response: {responseText}");
    }

    private static string GenerateFid()
    {
        var bytes = RandomNumberGenerator.GetBytes(17);
        // Set first 4 bits to 0111 (FID format)
        bytes[0] = (byte)(0x70 | (bytes[0] & 0x0F));
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=')[..22];
    }
}
