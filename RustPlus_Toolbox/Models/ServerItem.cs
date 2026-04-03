using System;
using System.Collections.Generic;
using System.Text;

namespace RustPlus_Toolbox.Models
{
    public class ServerItem
    {
        public int Id { get; set; }
        public bool Active { get; set; }
        public string Name { get; set; }
        public string RustPlusConfigPath { get; set; }
        public string ServerIP { get; set; }
        public int RustPlusPort { get; set; }
        public ulong SteamId { get; set; }
        public int PlayerToken { get; set; }
        public float BaseLocationX { get; set; }
        public float BaseLocationY { get; set; }
        public float Radius { get; set; }
        public List<ServerItemEntity> Entities { get; set; } = new List<ServerItemEntity>();

        /// <summary>
        /// Default Discord webhook settings for all alarms on this server.
        /// Entity-level settings override these when configured.
        /// </summary>
        public DiscordWebhookSettings? DiscordWebhook { get; set; }
    }

    public class ServerItemEntity
    {
        public uint EntityId { get; set; }
        public int EntityType { get; set; } // 1 = Smart Switch, 2 = Smart Alarm, 3 = Storage Monitor
        public bool? State { get; set; } = null;
        public string Name { get; set; }

        /// <summary>
        /// Entity-specific Discord webhook settings.
        /// When set, these override the server-level defaults for this entity.
        /// </summary>
        public DiscordWebhookSettings? DiscordWebhook { get; set; }
    }

    /// <summary>
    /// Discord webhook configuration for alarm notifications.
    /// </summary>
    public class DiscordWebhookSettings
    {
        /// <summary>Discord webhook URL.</summary>
        public string? WebhookUrl { get; set; }

        /// <summary>
        /// Custom message template. Use {title} and {message} as placeholders
        /// for the notification title and message from the alarm.
        /// Example: "🚨 RAID ALERT: {title} - {message}"
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// List of Discord user IDs to mention/tag in the message.
        /// Example: ["123456789012345678", "987654321098765432"]
        /// </summary>
        public List<string>? UserIds { get; set; }
    }
}
