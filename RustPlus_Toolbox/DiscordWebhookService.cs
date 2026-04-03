using Microsoft.Extensions.Logging;
using RustPlus_Toolbox.Models;
using System.Text;
using System.Text.Json;

namespace RustPlus_Toolbox
{
    /// <summary>
    /// Sends alarm notifications to Discord via webhooks.
    /// Supports server-level defaults with per-entity overrides.
    /// </summary>
    public sealed class DiscordWebhookService
    {
        private static readonly HttpClient _httpClient = new();
        private readonly ILogger _logger;

        public DiscordWebhookService(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Sends a Discord webhook notification for an alarm.
        /// Uses entity-level settings if configured, otherwise falls back to server-level settings.
        /// </summary>
        /// <param name="serverSettings">Server-level webhook settings (fallback).</param>
        /// <param name="entitySettings">Entity-level webhook settings (override), or null.</param>
        /// <param name="notificationTitle">The alarm notification title.</param>
        /// <param name="notificationMessage">The alarm notification message.</param>
        public async Task SendAlarmNotificationAsync(
            DiscordWebhookSettings? serverSettings,
            DiscordWebhookSettings? entitySettings,
            string notificationTitle,
            string notificationMessage)
        {
            // Entity settings override server settings; pick the effective config
            var settings = ResolveSettings(serverSettings, entitySettings);

            if (settings == null || string.IsNullOrWhiteSpace(settings.WebhookUrl))
            {
                _logger.LogDebug("Discord webhook not configured, skipping notification.");
                return;
            }

            try
            {
                // Build the message content
                string content = BuildMessageContent(settings, notificationTitle, notificationMessage);

                // Build the JSON payload
                var payload = new Dictionary<string, object>
                {
                    ["content"] = content
                };

                // If user IDs are configured, set allowed_mentions so Discord processes them
                if (settings.UserIds?.Count > 0)
                {
                    payload["allowed_mentions"] = new Dictionary<string, object>
                    {
                        ["users"] = settings.UserIds
                    };
                }

                var json = JsonSerializer.Serialize(payload);
                using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(settings.WebhookUrl, httpContent);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Discord webhook sent successfully: {Title}", notificationTitle);
                }
                else
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Discord webhook failed ({StatusCode}): {Response}",
                        response.StatusCode, responseBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Discord webhook for alarm: {Title}", notificationTitle);
            }
        }

        /// <summary>
        /// Resolves effective settings by using entity-level overrides where present,
        /// falling back to server-level defaults for unset fields.
        /// </summary>
        private static DiscordWebhookSettings? ResolveSettings(
            DiscordWebhookSettings? serverSettings,
            DiscordWebhookSettings? entitySettings)
        {
            // No settings at all
            if (serverSettings == null && entitySettings == null)
                return null;

            // Only one level configured
            if (entitySettings == null)
                return serverSettings;
            if (serverSettings == null)
                return entitySettings;

            // Merge: entity overrides server for each field
            return new DiscordWebhookSettings
            {
                WebhookUrl = !string.IsNullOrWhiteSpace(entitySettings.WebhookUrl)
                    ? entitySettings.WebhookUrl
                    : serverSettings.WebhookUrl,

                Message = !string.IsNullOrWhiteSpace(entitySettings.Message)
                    ? entitySettings.Message
                    : serverSettings.Message,

                UserIds = entitySettings.UserIds?.Count > 0
                    ? entitySettings.UserIds
                    : serverSettings.UserIds,
            };
        }

        /// <summary>
        /// Builds the final message content from the template and notification data.
        /// </summary>
        private static string BuildMessageContent(
            DiscordWebhookSettings settings,
            string notificationTitle,
            string notificationMessage)
        {
            // Start with the custom message template or a sensible default
            string template = !string.IsNullOrWhiteSpace(settings.Message)
                ? settings.Message
                : "\ud83d\udea8 **{title}** \u2014 {message}";

            // Replace placeholders
            string content = template
                .Replace("{title}", notificationTitle)
                .Replace("{message}", notificationMessage);

            // Append user mentions
            if (settings.UserIds?.Count > 0)
            {
                var mentions = string.Join(" ", settings.UserIds.Select(id => $"<@{id}>"));
                content = $"{content} {mentions}";
            }

            return content;
        }
    }
}
