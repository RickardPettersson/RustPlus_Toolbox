// Copyright (c) 2026 Rickard Nordström Pettersson. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Source: https://github.com/RickardPettersson/RustPlus_FCM

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Represents an FCM notification received from the Rust+ companion app.
/// </summary>
public sealed class RustPlusNotification
{
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("persistentId")]
    public string? PersistentId { get; set; }

    [JsonPropertyName("appData")]
    public Dictionary<string, string?>? AppData { get; set; }

    /// <summary>Notification title (from appData "title" key).</summary>
    public string? Title => AppData?.GetValueOrDefault("title");

    /// <summary>Notification message (from appData "message" key).</summary>
    public string? Message => AppData?.GetValueOrDefault("message");

    /// <summary>Notification channel (from appData "channelId" key).</summary>
    public string? ChannelId => AppData?.GetValueOrDefault("channelId");

    /// <summary>
    /// Parses the "body" appData value as a <see cref="RustPlusNotificationBody"/> if present.
    /// Returns null if the body is missing or not valid JSON.
    /// </summary>
    public RustPlusNotificationBody? ParseBody()
    {
        if (AppData is null || !AppData.TryGetValue("body", out var bodyJson) || bodyJson is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<RustPlusNotificationBody>(bodyJson);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Represents the info embedded in the "body" appData JSON string.
/// </summary>
public sealed class RustPlusNotificationBody
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("desc")]
    public string? Desc { get; set; }

    [JsonPropertyName("img")]
    public string? Img { get; set; }

    [JsonPropertyName("logo")]
    public string? Logo { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("ip")]
    public string? Ip { get; set; }

    [JsonPropertyName("port")]
    public string? Port { get; set; }

    [JsonPropertyName("playerId")]
    public string? PlayerId { get; set; }

    [JsonPropertyName("playerToken")]
    public string? PlayerToken { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("targetId")]
    public string? TargetId { get; set; }

    [JsonPropertyName("targetName")]
    public string? TargetName { get; set; }

    [JsonPropertyName("entityId")]
    public string? EntityId { get; set; }

    [JsonPropertyName("entityType")]
    public string? EntityType { get; set; }

    [JsonPropertyName("entityName")]
    public string? EntityName { get; set; }
}
