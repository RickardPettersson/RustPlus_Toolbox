// Copyright (c) 2026 Rickard Nordström Pettersson. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Source: https://github.com/RickardPettersson/RustPlus_FCM

using System.Text.Json;
using System.Text.Json.Nodes;

public static class ConfigManager
{
    public static JsonObject ReadConfig(string configFile)
    {
        try
        {
            var json = File.ReadAllText(configFile);
            return JsonNode.Parse(json)?.AsObject() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void UpdateConfig(string configFile, JsonObject newConfig)
    {
        var current = ReadConfig(configFile);

        foreach (var (key, value) in newConfig)
        {
            current[key] = value?.DeepClone();
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(configFile, current.ToJsonString(options));
    }
}
