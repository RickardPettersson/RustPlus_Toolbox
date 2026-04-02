// Copyright (c) 2026 Rickard Nordström Pettersson. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Source: https://github.com/RickardPettersson/RustPlus_FCM

using System.Net.Http.Json;
using System.Text.Json;

public static class ApiClient
{
    public static async Task<string> GetExpoPushTokenAsync(string fcmToken)
    {
        using var httpClient = new HttpClient();
        var payload = new
        {
            type = "fcm",
            deviceId = Guid.NewGuid().ToString(),
            development = false,
            appId = "com.facepunch.rust.companion",
            deviceToken = fcmToken,
            projectId = "49451aca-a822-41e6-ad59-955718d0ff9c",
        };

        var response = await httpClient.PostAsJsonAsync(
            "https://exp.host/--/api/v2/push/getExpoPushToken", payload);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("expoPushToken").GetString()
            ?? throw new InvalidOperationException("Failed to get Expo push token from response.");
    }

    public static async Task RegisterWithRustPlusAsync(string authToken, string expoPushToken)
    {
        using var httpClient = new HttpClient();
        var payload = new
        {
            AuthToken = authToken,
            DeviceId = "rustplus.js",
            PushKind = 3,
            PushToken = expoPushToken,
        };

        var response = await httpClient.PostAsJsonAsync(
            "https://companion-rust.facepunch.com:443/api/push/register", payload);
        response.EnsureSuccessStatusCode();
    }
}
