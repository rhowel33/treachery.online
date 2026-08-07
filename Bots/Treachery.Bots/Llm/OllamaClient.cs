/*
 * Copyright (C) 2020-2025 Ronald Ossendrijver (admin@treachery.online)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This
 * program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have
 * received a copy of the GNU General Public License along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Text;
using System.Text.Json.Nodes;

namespace Treachery.Bots;

/// <summary>
/// Minimal client for the Ollama /api/chat endpoint, supporting structured output via a JSON schema.
/// </summary>
public class OllamaClient(string baseUrl, string model)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public string Model => model;

    /// <summary>
    /// Sends a chat request and returns the raw content of the reply message.
    /// When <paramref name="formatSchema"/> is given, Ollama constrains the reply to that JSON schema.
    /// </summary>
    public async Task<string?> ChatAsync(string systemPrompt, IEnumerable<(string Role, string Content)> messages, JsonNode? formatSchema = null)
    {
        var messageArray = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = systemPrompt } };
        foreach (var (role, content) in messages)
            messageArray.Add(new JsonObject { ["role"] = role, ["content"] = content });

        var request = new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
            ["messages"] = messageArray,
            ["options"] = new JsonObject { ["temperature"] = 0.2 }
        };

        if (formatSchema != null)
            request["format"] = formatSchema;

        using var body = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync($"{baseUrl.TrimEnd('/')}/api/chat", body);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(responseText)?["message"]?["content"]?.GetValue<string>();
    }
}
