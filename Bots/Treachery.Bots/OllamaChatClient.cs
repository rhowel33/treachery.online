/*
 * Copyright (C) 2020-2025 Ronald Ossendrijver (admin@treachery.online)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This
 * program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have
 * received a copy of the GNU General Public License along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Treachery.Bots;

/// <summary>
/// Ollama chat client with function calling support for gpt-oss:20b
/// </summary>
public class OllamaChatClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _model;

    public OllamaChatClient(string baseUrl = "http://localhost:11434", string model = "gpt-oss:20b")
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
    }

    public async Task<OllamaChatResponse> ChatAsync(
        List<OllamaChatMessage> messages,
        List<OllamaTool>? tools = null,
        CancellationToken cancellationToken = default)
    {
        var request = new OllamaChatRequest
        {
            Model = _model,
            Messages = messages,
            Tools = tools,
            Stream = false,
            Options = new OllamaOptions
            {
                Temperature = 0.1f, // Low temperature for deterministic decisions
                TopP = 0.9f,
                NumPredict = 2000  // Increased for tool calling
            }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/chat",
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken);
            return result ?? throw new Exception("Empty response from Ollama");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to get chat response from Ollama: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

public record OllamaChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("messages")]
    public List<OllamaChatMessage> Messages { get; init; } = new();

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OllamaTool>? Tools { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; } = false;

    [JsonPropertyName("options")]
    public OllamaOptions? Options { get; init; }
}

public record OllamaChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = ""; // "system", "user", "assistant", "tool"

    [JsonPropertyName("content")]
    public string Content { get; init; } = "";

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OllamaToolCall>? ToolCalls { get; init; }
}

public record OllamaToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public OllamaFunction Function { get; init; } = new();
}

public record OllamaFunction
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("arguments")]
    public string Arguments { get; init; } = ""; // JSON string
}

public record OllamaTool
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public OllamaToolFunction Function { get; init; } = new();
}

public record OllamaToolFunction
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("parameters")]
    public object Parameters { get; init; } = new { };
}

public record OllamaChatResponse
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("message")]
    public OllamaChatMessage Message { get; init; } = new();

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    [JsonPropertyName("total_duration")]
    public long TotalDuration { get; init; }

    [JsonPropertyName("load_duration")]
    public long LoadDuration { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int PromptEvalCount { get; init; }

    [JsonPropertyName("prompt_eval_duration")]
    public long PromptEvalDuration { get; init; }

    [JsonPropertyName("eval_count")]
    public int EvalCount { get; init; }

    [JsonPropertyName("eval_duration")]
    public long EvalDuration { get; init; }
}
